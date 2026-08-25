using Azure;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using RndKnowledgeMining.Mcp.Models;
using RndKnowledgeMining.Mcp.Options;
using Microsoft.Extensions.Options;

namespace RndKnowledgeMining.Mcp.Adapters;

public sealed class SearchIndexInitializer
{
    private readonly SearchIndexClient _indexClient;
    private readonly AzureSearchOptions _options;

    public SearchIndexInitializer(SearchIndexClient indexClient, IOptions<AzureSearchOptions> options)
    {
        _indexClient = indexClient;
        _options = options.Value;
    }

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureKnowledgeIndexAsync(cancellationToken);
        await EnsurePolicyIndexAsync(cancellationToken);
    }

    private async Task EnsureKnowledgeIndexAsync(CancellationToken cancellationToken)
    {
        string indexName = _options.KnowledgeIndexName;

        var fields = new List<SearchField>
        {
            new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
            new SearchableField("entityId") { IsFilterable = true, IsFacetable = true },
            new SearchableField("entityType") { IsFilterable = true, IsFacetable = true },
            new SearchableField("title") { IsFilterable = false },
            new SearchableField("passageId") { IsFilterable = true },
            new SearchableField("chunkText") { IsFilterable = false },
            new SearchableField("lineageNarrative") { IsFilterable = false },
            new SearchField("linkedEntities", SearchFieldDataType.Collection(SearchFieldDataType.String))
            {
                IsFilterable = true,
                IsFacetable = true
            },
            new SimpleField("indexedAt", SearchFieldDataType.DateTimeOffset)
            {
                IsFilterable = true,
                IsSortable = true
            },
            new SearchField("embedding", SearchFieldDataType.Collection(SearchFieldDataType.Single))
            {
                IsSearchable = true,
                VectorSearchDimensions = _options.VectorDimensions,
                VectorSearchProfileName = "default-vector-profile"
            }
        };

        var index = new SearchIndex(indexName)
        {
            Fields = fields,
            VectorSearch = new VectorSearch
            {
                Profiles = { new VectorSearchProfile("default-vector-profile", "default-hnsw-config") },
                Algorithms = { new HnswAlgorithmConfiguration("default-hnsw-config") }
            }
        };

        await _indexClient.CreateOrUpdateIndexAsync(index, allowIndexDowntime: true, cancellationToken: cancellationToken);
    }

    private async Task EnsurePolicyIndexAsync(CancellationToken cancellationToken)
    {
        string indexName = _options.PolicyIndexName;
        if (await IndexExistsAsync(indexName, cancellationToken))
        {
            return;
        }

        var fields = new List<SearchField>
        {
            new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
            new SearchableField("policyRef") { IsFilterable = true, IsFacetable = true },
            new SearchableField("documentType") { IsFilterable = true, IsFacetable = true },
            new SearchableField("rule") { IsFilterable = false },
            new SearchableField("threshold") { IsFilterable = false },
            new SearchableField("action") { IsFilterable = false },
            new SearchableField("exception") { IsFilterable = false },
            new SearchableField("fullText") { IsFilterable = false },
            new SearchableField("contentHash") { IsFilterable = true },
            new SearchField("embedding", SearchFieldDataType.Collection(SearchFieldDataType.Single))
            {
                IsSearchable = true,
                VectorSearchDimensions = _options.VectorDimensions,
                VectorSearchProfileName = "default-vector-profile"
            }
        };

        var index = new SearchIndex(indexName)
        {
            Fields = fields,
            VectorSearch = new VectorSearch
            {
                Profiles = { new VectorSearchProfile("default-vector-profile", "default-hnsw-config") },
                Algorithms = { new HnswAlgorithmConfiguration("default-hnsw-config") }
            }
        };

        await _indexClient.CreateIndexAsync(index, cancellationToken);
    }

    private async Task<bool> IndexExistsAsync(string indexName, CancellationToken cancellationToken)
    {
        try
        {
            await _indexClient.GetIndexAsync(indexName, cancellationToken);
            return true;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return false;
        }
    }
}

public static class SearchClientFactory
{
    public static SearchIndexClient CreateIndexClient(AzureSearchOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Endpoint);
        return new SearchIndexClient(new Uri(options.Endpoint), new DefaultAzureCredential());
    }
}
