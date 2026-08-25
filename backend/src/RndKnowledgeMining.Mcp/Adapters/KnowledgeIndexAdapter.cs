using Azure;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using RndKnowledgeMining.Mcp.Models;
using RndKnowledgeMining.Mcp.Options;
using Microsoft.Extensions.Options;

namespace RndKnowledgeMining.Mcp.Adapters;

public sealed class KnowledgeIndexAdapter
{
    private static readonly Regex SearchKeyInvalidChars = new("[^A-Za-z0-9_\\-=]", RegexOptions.Compiled);

    private readonly SearchClient _searchClient;
    private readonly FoundryEmbeddingService _embeddingService;
    private readonly FoundryRerankService _rerankService;

    public KnowledgeIndexAdapter(
        SearchIndexClient indexClient,
        FoundryEmbeddingService embeddingService,
        FoundryRerankService rerankService,
        IOptions<AzureSearchOptions> options)
    {
        _searchClient = indexClient.GetSearchClient(options.Value.KnowledgeIndexName);
        _embeddingService = embeddingService;
        _rerankService = rerankService;
    }

    public async Task<SearchRdKnowledgeResponse> SearchAsync(
        string sessionId,
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        var queryEmbedding = (await _embeddingService.EmbedAsync([query], cancellationToken)).Single();
        var searchOptions = new SearchOptions
        {
            Size = Math.Max(topK * 3, topK),
            Filter = "entityType ne 'metadata'",
            Select = { "entityId", "entityType", "title", "passageId", "chunkText" }
        };

        searchOptions.VectorSearch = new VectorSearchOptions
        {
            Queries =
            {
                new VectorizedQuery(queryEmbedding)
                {
                    KNearestNeighborsCount = searchOptions.Size,
                    Fields = { "embedding" }
                }
            }
        };

        var response = await _searchClient.SearchAsync<SearchDocument>(null, searchOptions, cancellationToken);
        var candidates = new List<KnowledgeSearchCandidate>();

        await foreach (var result in response.Value.GetResultsAsync())
        {
            if (result.Document is null)
            {
                continue;
            }

            candidates.Add(new KnowledgeSearchCandidate
            {
                EntityId = GetString(result.Document, "entityId"),
                EntityType = GetString(result.Document, "entityType"),
                Title = GetString(result.Document, "title"),
                PassageId = GetString(result.Document, "passageId"),
                ChunkText = GetString(result.Document, "chunkText")
            });
        }

        if (candidates.Count == 0)
        {
            return new SearchRdKnowledgeResponse
            {
                SessionId = sessionId,
                Query = query,
                Matches = []
            };
        }

        IReadOnlyList<KnowledgePassageMatch> matches;
        if (candidates.Count <= topK)
        {
            matches = candidates.Select(candidate => ToMatch(candidate)).ToArray();
        }
        else
        {
            var reranked = await _rerankService.RerankAsync(
                query,
                candidates.Select(candidate => $"{candidate.Title}\n{candidate.ChunkText}").ToArray(),
                topK,
                cancellationToken);

            matches = reranked
                .Select(result => ToMatch(candidates[result.Index], result.Score))
                .ToArray();
        }

        return new SearchRdKnowledgeResponse
        {
            SessionId = sessionId,
            Query = query,
            Matches = matches
        };
    }

    public async Task<IndexRdKnowledgeResponse> IndexAsync(
        string sessionId,
        string entityId,
        string entityType,
        string title,
        string chunkText,
        IReadOnlyList<string>? linkedEntities = null,
        string? lineageNarrative = null,
        string? passageId = null,
        CancellationToken cancellationToken = default)
    {
        var batchResponse = await IndexBatchAsync(
            sessionId,
            [
                new IndexRdKnowledgeBatchItem
                {
                    EntityId = entityId,
                    EntityType = entityType,
                    Title = title,
                    ChunkText = chunkText,
                    PassageId = passageId,
                    LinkedEntities = linkedEntities,
                    LineageNarrative = lineageNarrative
                }
            ],
            cancellationToken);

        return batchResponse.Results[0];
    }

    public async Task<IndexRdKnowledgeBatchResponse> IndexBatchAsync(
        string sessionId,
        IReadOnlyList<IndexRdKnowledgeBatchItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new ArgumentException("At least one indexing item is required.", nameof(items));
        }

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            ArgumentException.ThrowIfNullOrWhiteSpace(item.EntityId);
            ArgumentException.ThrowIfNullOrWhiteSpace(item.EntityType);
            ArgumentException.ThrowIfNullOrWhiteSpace(item.Title);
            ArgumentException.ThrowIfNullOrWhiteSpace(item.ChunkText);
        }

        if (items.Count > ToolResponseLimits.MaxIndexBatchItems)
        {
            throw new ArgumentException(
                $"index_rd_knowledge_batch accepts at most {ToolResponseLimits.MaxIndexBatchItems} items per call. Split larger embedding plans into multiple batch calls.",
                nameof(items));
        }

        var normalizedItems = items
            .Select(item => new IndexRdKnowledgeBatchItem
            {
                EntityId = item.EntityId.Trim(),
                EntityType = item.EntityType.Trim(),
                Title = item.Title.Trim(),
                ChunkText = TruncateChunkText(item.ChunkText),
                PassageId = item.PassageId,
                LinkedEntities = item.LinkedEntities,
                LineageNarrative = item.LineageNarrative
            })
            .ToList();

        var chunkTexts = normalizedItems
            .Select(item => item.ChunkText)
            .ToArray();

        var embeddings = await _embeddingService.EmbedAsync(chunkTexts, cancellationToken);
        var documents = new List<KnowledgeSearchDocument>(normalizedItems.Count);

        for (var index = 0; index < normalizedItems.Count; index++)
        {
            var item = normalizedItems[index];
            var normalizedPassageId = NormalizePassageId(item.EntityId, item.ChunkText, item.PassageId);
            var documentKey = NormalizeDocumentKey(item.EntityId, normalizedPassageId);
            var normalizedLinkedEntities = (item.LinkedEntities ?? [])
                .Where(linkedEntity => !string.IsNullOrWhiteSpace(linkedEntity))
                .Select(linkedEntity => linkedEntity.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            documents.Add(new KnowledgeSearchDocument
            {
                Id = documentKey,
                EntityId = item.EntityId.Trim(),
                EntityType = item.EntityType.Trim(),
                Title = item.Title.Trim(),
                PassageId = normalizedPassageId,
                ChunkText = item.ChunkText.Trim(),
                LinkedEntities = normalizedLinkedEntities,
                LineageNarrative = string.IsNullOrWhiteSpace(item.LineageNarrative)
                    ? string.Empty
                    : item.LineageNarrative.Trim(),
                Embedding = embeddings[index],
                IndexedAt = DateTimeOffset.UtcNow
            });
        }

        var batch = IndexDocumentsBatch.MergeOrUpload(documents);
        var response = await _searchClient.IndexDocumentsAsync(batch, cancellationToken: cancellationToken);
        var resultsByKey = response.Value.Results
            .ToDictionary(result => result.Key, StringComparer.OrdinalIgnoreCase);

        var results = new List<IndexRdKnowledgeResponse>(documents.Count);
        var indexedCount = 0;

        foreach (var document in documents)
        {
            var indexed = resultsByKey.TryGetValue(document.Id, out IndexingResult? indexingResult)
                && indexingResult.Succeeded;

            if (indexed)
            {
                indexedCount++;
            }

            results.Add(new IndexRdKnowledgeResponse
            {
                SessionId = sessionId.Trim(),
                EntityId = document.EntityId,
                PassageId = document.PassageId,
                Indexed = indexed
            });
        }

        if (indexedCount < documents.Count)
        {
            var failures = response.Value.Results
                .Where(result => !result.Succeeded)
                .Select(result => $"{result.Key}: {result.ErrorMessage}")
                .ToArray();

            throw new InvalidOperationException(
                $"Failed to index all passages for session '{sessionId}'. Indexed {indexedCount} of {documents.Count}. " +
                (failures.Length == 0 ? string.Empty : $"Failures: {string.Join(" | ", failures)}"));
        }

        return new IndexRdKnowledgeBatchResponse
        {
            SessionId = sessionId.Trim(),
            Requested = documents.Count,
            Indexed = indexedCount,
            Results = results
        };
    }

    public async Task<KnowledgeLineageResponse> GetLineageAsync(
        string sessionId,
        string passageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(passageId);

        string normalizedPassageId = passageId.Trim();
        string escapedPassageId = normalizedPassageId.Replace("'", "''", StringComparison.Ordinal);
        var filterResponse = await _searchClient.SearchAsync<KnowledgeSearchDocument>(
            null,
            new SearchOptions
            {
                Size = 1,
                Filter = $"passageId eq '{escapedPassageId}' and entityType ne 'metadata'",
                Select = { "entityId", "entityType", "passageId", "linkedEntities", "lineageNarrative" }
            },
            cancellationToken);

        await foreach (SearchResult<KnowledgeSearchDocument> result in filterResponse.Value.GetResultsAsync())
        {
            if (result.Document is null)
            {
                continue;
            }

            return new KnowledgeLineageResponse
            {
                SessionId = sessionId.Trim(),
                PassageId = result.Document.PassageId,
                EntityId = result.Document.EntityId,
                LinkedEntities = result.Document.LinkedEntities,
                Lineage = result.Document.LineageNarrative
            };
        }

        throw new KeyNotFoundException($"No lineage record was found for passage '{passageId}'.");
    }

    public async Task<KnowledgeIndexSummaryResult> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var totalCount = await _searchClient.GetDocumentCountAsync(cancellationToken);

        var searchOptions = new SearchOptions
        {
            Size = 0,
            IncludeTotalCount = true
        };
        searchOptions.Facets.Add("entityType");

        var response = await _searchClient.SearchAsync<SearchDocument>("*", searchOptions, cancellationToken);

        long totalDocuments = 0, totalStudies = 0;

        if (response.Value.Facets?.TryGetValue("entityType", out var entityTypeFacets) == true)
        {
            foreach (var facet in entityTypeFacets)
            {
                var type = facet.Value?.ToString();
                var count = facet.Count ?? 0;
                if (string.Equals(type, "document", StringComparison.OrdinalIgnoreCase))
                {
                    totalDocuments = count;
                }
                else if (type is not null
                    && !string.Equals(type, "metadata", StringComparison.OrdinalIgnoreCase))
                {
                    totalStudies += count;
                }
            }
        }

        DateTimeOffset? lastIngestionAt = null;
        string? lastIngestedId = null;

        try
        {
            var latestOptions = new SearchOptions
            {
                Size = 1,
                OrderBy = { "indexedAt desc" },
                Select = { "entityId", "indexedAt" }
            };

            var latestResponse = await _searchClient.SearchAsync<SearchDocument>("*", latestOptions, cancellationToken);
            await foreach (var result in latestResponse.Value.GetResultsAsync())
            {
                if (result.Document.TryGetValue("indexedAt", out object? ts) && ts is DateTimeOffset dto)
                {
                    lastIngestionAt = dto;
                }

                lastIngestedId = GetString(result.Document, "entityId");
            }
        }
        catch (RequestFailedException)
        {
            // indexedAt field may not exist in the index schema yet; gracefully return null.
        }

        return new KnowledgeIndexSummaryResult(
            TotalDocuments: totalDocuments,
            TotalStudies: totalStudies,
            TotalEntities: totalCount,
            TotalLinks: 0,
            LastIngestionAt: lastIngestionAt,
            LastIngestedStudyId: lastIngestedId);
    }

    /// <summary>
    /// Deletes every document from the knowledge evidence index.
    /// </summary>
    public async Task<long> ClearAllAsync(CancellationToken cancellationToken = default)
    {
        const int pageSize = 1000;
        const int maxPasses = 100;
        long deletedCount = 0;

        for (int pass = 0; pass < maxPasses; pass++)
        {
            var searchOptions = new SearchOptions
            {
                Size = pageSize,
                Select = { "id" }
            };

            var response = await _searchClient
                .SearchAsync<SearchDocument>("*", searchOptions, cancellationToken)
                .ConfigureAwait(false);

            var keys = new List<string>(pageSize);
            await foreach (SearchResult<SearchDocument> result in response.Value.GetResultsAsync().ConfigureAwait(false))
            {
                string? id = GetString(result.Document, "id");
                if (!string.IsNullOrWhiteSpace(id))
                {
                    keys.Add(id);
                }
            }

            if (keys.Count == 0)
            {
                return deletedCount;
            }

            var batch = IndexDocumentsBatch.Delete("id", keys);
            var deleteResponse = await _searchClient
                .IndexDocumentsAsync(batch, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            int batchDeleted = deleteResponse.Value.Results.Count(result => result.Succeeded);
            deletedCount += batchDeleted;

            if (batchDeleted < keys.Count)
            {
                var failures = deleteResponse.Value.Results
                    .Where(result => !result.Succeeded)
                    .Select(result => $"{result.Key}: {result.ErrorMessage}")
                    .ToArray();

                throw new InvalidOperationException(
                    $"Failed to delete all documents from the knowledge evidence index. " +
                    $"Deleted {deletedCount} so far. Failures: {string.Join(" | ", failures)}");
            }
        }

        throw new InvalidOperationException(
            $"Knowledge evidence index clear did not finish after {maxPasses} passes. Deleted {deletedCount} documents.");
    }

    private static KnowledgePassageMatch ToMatch(KnowledgeSearchCandidate candidate, double score = 1) =>
        new()
        {
            PassageId = candidate.PassageId ?? string.Empty,
            EntityId = candidate.EntityId ?? string.Empty,
            EntityType = candidate.EntityType ?? string.Empty,
            Title = candidate.Title ?? string.Empty,
            Snippet = Truncate(candidate.ChunkText ?? string.Empty),
            Score = score
        };

    private static string Truncate(string text) =>
        text.Length <= ToolResponseLimits.MaxEvidenceSnippetLength
            ? text
            : text[..ToolResponseLimits.MaxEvidenceSnippetLength] + "...";

    private static string TruncateChunkText(string chunkText)
    {
        string trimmed = chunkText.Trim();
        return trimmed.Length <= ToolResponseLimits.MaxIndexChunkTextLength
            ? trimmed
            : string.Concat(trimmed.AsSpan(0, ToolResponseLimits.MaxIndexChunkTextLength), "...");
    }

    private static string? GetString(SearchDocument document, string fieldName)
    {
        if (!document.TryGetValue(fieldName, out object? value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            _ => value.ToString()
        };
    }

    private static string NormalizePassageId(string entityId, string chunkText, string? passageId)
    {
        if (!string.IsNullOrWhiteSpace(passageId))
        {
            return passageId.Trim();
        }

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(chunkText.Trim())));

        return $"{entityId.Trim()}-{hash[..12].ToLowerInvariant()}";
    }

    private static string NormalizeDocumentKey(string entityId, string passageId)
    {
        string source = string.IsNullOrWhiteSpace(entityId) ? passageId : entityId;
        string normalized = SearchKeyInvalidChars.Replace(source.Trim(), "-");
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "doc";
        }

        if (!char.IsLetterOrDigit(normalized[0]))
        {
            normalized = "d" + normalized;
        }

        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(passageId.Trim()))).ToLowerInvariant();

        // Azure AI Search key supports A-Z, a-z, 0-9, '_', '-', '='.
        return $"{normalized[..Math.Min(normalized.Length, 64)]}-{hash[..16]}";
    }

    private sealed class KnowledgeSearchCandidate
    {
        public string? EntityId { get; init; }

        public string? EntityType { get; init; }

        public string? Title { get; init; }

        public string? PassageId { get; init; }

        public string? ChunkText { get; init; }
    }

    public sealed class KnowledgeSearchDocument
    {
        public required string Id { get; set; }

        public required string EntityId { get; set; }

        public required string EntityType { get; set; }

        public required string Title { get; set; }

        public required string PassageId { get; set; }

        public required string ChunkText { get; set; }

        public IReadOnlyList<string> LinkedEntities { get; set; } = [];

        public string LineageNarrative { get; set; } = string.Empty;

        public IReadOnlyList<float> Embedding { get; set; } = [];

        [JsonPropertyName("indexedAt")]
        public DateTimeOffset? IndexedAt { get; set; }
    }
}
