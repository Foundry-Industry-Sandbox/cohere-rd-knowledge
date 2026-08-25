using System.Text.Json;
using Microsoft.Extensions.Logging;
using RndKnowledgeMining.Mcp.Models;

namespace RndKnowledgeMining.Mcp.Adapters;

public sealed class LocalNormalizedDocumentStore : INormalizedDocumentStore
{
    private readonly string _datasetRoot;
    private readonly ILogger<LocalNormalizedDocumentStore> _logger;

    public LocalNormalizedDocumentStore(string datasetRoot, ILogger<LocalNormalizedDocumentStore> logger)
    {
        _datasetRoot = datasetRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _logger = logger;
    }

    public async Task<string> PersistIngestionHandoffAsync(
        string sourceId,
        string executionId,
        string ingestionPayloadJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ingestionPayloadJson);

        NormalizedDocumentHandoffSplitter.SplitResult split =
            NormalizedDocumentHandoffSplitter.Split(ingestionPayloadJson);
        string manifestJson = NormalizedManifestPatcher.ApplyWorkflowExecutionId(split.ManifestJson, executionId);
        string resolvedExecutionId = NormalizedExecutionIdResolver.Normalize(executionId);

        string batchDirectory = NormalizedDocumentPathBuilder.BuildLocalBatchDirectory(_datasetRoot, sourceId, resolvedExecutionId);
        string documentsDirectory = Path.Combine(batchDirectory, "documents");
        Directory.CreateDirectory(documentsDirectory);

        foreach (NormalizedDocumentHandoffSplitter.NormalizedDocumentArtifact document in split.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string documentPath = NormalizedDocumentPathBuilder.BuildLocalDocumentPath(
                _datasetRoot,
                sourceId,
                resolvedExecutionId,
                document.DocumentId);
            await File.WriteAllTextAsync(documentPath, document.DocumentJson, cancellationToken).ConfigureAwait(false);
        }

        string manifestPath = NormalizedDocumentPathBuilder.BuildLocalManifestPath(_datasetRoot, sourceId, resolvedExecutionId);
        await File.WriteAllTextAsync(manifestPath, manifestJson, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Persisted {DocumentCount} normalized documents for source {SourceId}, execution {ExecutionId} under {BatchDirectory}.",
            split.Documents.Count,
            sourceId,
            resolvedExecutionId,
            batchDirectory);

        return manifestJson;
    }

    public Task<ListNormalizedDocumentsResponse> ListAsync(
        string sourceId,
        string executionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);

        string resolvedExecutionId = ResolveExecutionIdForList(sourceId, executionId);
        string batchDirectory = NormalizedDocumentPathBuilder.BuildLocalBatchDirectory(_datasetRoot, sourceId, resolvedExecutionId);
        string normalizedRoot = Path.Combine(_datasetRoot, "cases", sourceId, "normalized", resolvedExecutionId)
            .Replace('\\', '/');

        if (!Directory.Exists(batchDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Normalized batch directory not found: {batchDirectory}");
        }

        string manifestPath = NormalizedDocumentPathBuilder.BuildLocalManifestPath(_datasetRoot, sourceId, resolvedExecutionId);
        string manifestJson = File.ReadAllText(manifestPath);
        using JsonDocument manifest = JsonDocument.Parse(manifestJson);

        var summaries = new List<NormalizedDocumentSummary>();
        if (manifest.RootElement.TryGetProperty("documentSummaries", out JsonElement summariesElement)
            && summariesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement summaryElement in summariesElement.EnumerateArray())
            {
                string? documentId = summaryElement.TryGetProperty("documentId", out JsonElement idElement)
                    ? idElement.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(documentId))
                {
                    continue;
                }

                summaries.Add(new NormalizedDocumentSummary
                {
                    DocumentId = documentId,
                    SourceFile = ReadOptionalString(summaryElement, "sourceFile"),
                    CanonicalType = ReadOptionalString(summaryElement, "canonicalType"),
                    StoragePath = NormalizedDocumentPathBuilder.BuildLocalDocumentPath(
                        _datasetRoot,
                        sourceId,
                        resolvedExecutionId,
                        documentId).Replace('\\', '/')
                });
            }
        }

        int documentsReceived = manifest.RootElement.TryGetProperty("documentsReceived", out JsonElement countElement)
            && countElement.TryGetInt32(out int count)
            ? count
            : summaries.Count;

        return Task.FromResult(new ListNormalizedDocumentsResponse
        {
            SourceId = sourceId,
            ExecutionId = resolvedExecutionId,
            NormalizedRoot = normalizedRoot,
            DocumentsReceived = documentsReceived,
            Documents = summaries
        });
    }

    public Task<ReadNormalizedDocumentResponse> ReadAsync(
        string sourceId,
        string executionId,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        string resolvedExecutionId = ResolveExecutionIdForRead(sourceId, executionId, documentId);
        string documentPath = NormalizedDocumentPathBuilder.BuildLocalDocumentPath(
            _datasetRoot,
            sourceId,
            resolvedExecutionId,
            documentId);

        if (!File.Exists(documentPath))
        {
            throw new FileNotFoundException(
                $"Normalized document '{documentId}' was not found for source '{sourceId}', execution '{executionId}'.");
        }

        string content = File.ReadAllText(documentPath);
        return Task.FromResult(new ReadNormalizedDocumentResponse
        {
            SourceId = sourceId,
            ExecutionId = resolvedExecutionId,
            DocumentId = documentId,
            StoragePath = documentPath.Replace('\\', '/'),
            Content = NormalizedDocumentAgentView.Compact(content)
        });
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private string ResolveExecutionIdForList(string sourceId, string requestedExecutionId)
    {
        string normalizedRequested = NormalizedExecutionIdResolver.Normalize(requestedExecutionId);
        string manifestPath = NormalizedDocumentPathBuilder.BuildLocalManifestPath(_datasetRoot, sourceId, normalizedRequested);
        if (File.Exists(manifestPath))
        {
            return normalizedRequested;
        }

        string? resolved = TryResolveExecutionId(sourceId, normalizedRequested, documentId: null);
        if (resolved is not null)
        {
            _logger.LogWarning(
                "Resolved mistyped executionId {RequestedExecutionId} to {ResolvedExecutionId} for source {SourceId} during list_normalized_documents.",
                requestedExecutionId,
                resolved,
                sourceId);
            return resolved;
        }

        throw new DirectoryNotFoundException(
            $"Normalized batch directory not found for source '{sourceId}' and execution '{requestedExecutionId}'.");
    }

    private string ResolveExecutionIdForRead(string sourceId, string requestedExecutionId, string documentId)
    {
        string normalizedRequested = NormalizedExecutionIdResolver.Normalize(requestedExecutionId);
        string documentPath = NormalizedDocumentPathBuilder.BuildLocalDocumentPath(
            _datasetRoot,
            sourceId,
            normalizedRequested,
            documentId);
        if (File.Exists(documentPath))
        {
            return normalizedRequested;
        }

        string? resolved = TryResolveExecutionId(sourceId, normalizedRequested, documentId);
        if (resolved is not null)
        {
            _logger.LogWarning(
                "Resolved mistyped executionId {RequestedExecutionId} to {ResolvedExecutionId} for source {SourceId}, document {DocumentId} during read_normalized_document.",
                requestedExecutionId,
                resolved,
                sourceId,
                documentId);
            return resolved;
        }

        return normalizedRequested;
    }

    private string? TryResolveExecutionId(string sourceId, string requestedExecutionId, string? documentId)
    {
        string normalizedRoot = Path.Combine(_datasetRoot, "cases", sourceId, "normalized");
        if (!Directory.Exists(normalizedRoot))
        {
            return null;
        }

        IReadOnlyList<string> availableExecutionIds = Directory
            .EnumerateDirectories(normalizedRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();

        string? closest = NormalizedExecutionIdResolver.FindClosestMatch(requestedExecutionId, availableExecutionIds);
        if (closest is not null)
        {
            return NormalizedExecutionIdResolver.Normalize(closest);
        }

        if (string.IsNullOrWhiteSpace(documentId))
        {
            return null;
        }

        string fileName = NormalizedDocumentPathBuilder.ToStorageFileName(documentId);
        foreach (string executionDirectory in Directory.EnumerateDirectories(normalizedRoot))
        {
            string candidatePath = Path.Combine(executionDirectory, "documents", fileName);
            if (!File.Exists(candidatePath))
            {
                continue;
            }

            string? folderName = Path.GetFileName(executionDirectory);
            if (!string.IsNullOrWhiteSpace(folderName)
                && NormalizedExecutionIdResolver.IsValidFormat(folderName))
            {
                return NormalizedExecutionIdResolver.Normalize(folderName);
            }
        }

        return null;
    }
}
