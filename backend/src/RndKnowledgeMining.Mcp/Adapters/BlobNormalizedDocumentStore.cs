using System.Text;
using System.Text.Json;
using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RndKnowledgeMining.Mcp.Models;
using RndKnowledgeMining.Mcp.Options;

namespace RndKnowledgeMining.Mcp.Adapters;

public sealed class BlobNormalizedDocumentStore : INormalizedDocumentStore
{
    private readonly BlobContainerClient _containerClient;
    private readonly string _normalizedPrefix;
    private readonly ILogger<BlobNormalizedDocumentStore> _logger;

    public BlobNormalizedDocumentStore(
        IOptions<AzureBlobStorageOptions> options,
        ILogger<BlobNormalizedDocumentStore> logger)
    {
        AzureBlobStorageOptions blobOptions = options.Value;
        _normalizedPrefix = string.IsNullOrWhiteSpace(blobOptions.NormalizedPrefix)
            ? "normalized"
            : blobOptions.NormalizedPrefix.Trim('/');
        _logger = logger;

        if (string.IsNullOrWhiteSpace(blobOptions.ContainerName))
        {
            throw new InvalidOperationException("AzureStorage:ContainerName is required when using blob normalized storage.");
        }

        _containerClient = CreateBlobServiceClient(blobOptions)
            .GetBlobContainerClient(blobOptions.ContainerName);
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

        foreach (NormalizedDocumentHandoffSplitter.NormalizedDocumentArtifact document in split.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string blobPath = NormalizedDocumentPathBuilder.BuildDocumentRelativePath(
                _normalizedPrefix,
                sourceId,
                resolvedExecutionId,
                document.DocumentId);
            await UploadTextAsync(blobPath, document.DocumentJson, cancellationToken).ConfigureAwait(false);
        }

        string manifestPath = NormalizedDocumentPathBuilder.BuildManifestRelativePath(
            _normalizedPrefix,
            sourceId,
            resolvedExecutionId);
        await UploadTextAsync(manifestPath, manifestJson, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Persisted {DocumentCount} normalized documents for source {SourceId}, execution {ExecutionId} in container {ContainerName} under {NormalizedRoot}.",
            split.Documents.Count,
            sourceId,
            resolvedExecutionId,
            _containerClient.Name,
            NormalizedDocumentPathBuilder.BuildBatchRoot(_normalizedPrefix, sourceId, resolvedExecutionId));

        return manifestJson;
    }

    public async Task<ListNormalizedDocumentsResponse> ListAsync(
        string sourceId,
        string executionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);

        string resolvedExecutionId = await ResolveExecutionIdForListAsync(sourceId, executionId, cancellationToken)
            .ConfigureAwait(false);

        string manifestPath = NormalizedDocumentPathBuilder.BuildManifestRelativePath(
            _normalizedPrefix,
            sourceId,
            resolvedExecutionId);
        string manifestJson = await DownloadTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
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
                    StoragePath = NormalizedDocumentPathBuilder.BuildDocumentRelativePath(
                        _normalizedPrefix,
                        sourceId,
                        resolvedExecutionId,
                        documentId)
                });
            }
        }

        int documentsReceived = manifest.RootElement.TryGetProperty("documentsReceived", out JsonElement countElement)
            && countElement.TryGetInt32(out int count)
            ? count
            : summaries.Count;

        return new ListNormalizedDocumentsResponse
        {
            SourceId = sourceId,
            ExecutionId = resolvedExecutionId,
            NormalizedRoot = NormalizedDocumentPathBuilder.BuildBatchRoot(_normalizedPrefix, sourceId, resolvedExecutionId),
            DocumentsReceived = documentsReceived,
            Documents = summaries
        };
    }

    public async Task<ReadNormalizedDocumentResponse> ReadAsync(
        string sourceId,
        string executionId,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        string resolvedExecutionId = await ResolveExecutionIdForReadAsync(
                sourceId,
                executionId,
                documentId,
                cancellationToken)
            .ConfigureAwait(false);

        string blobPath = NormalizedDocumentPathBuilder.BuildDocumentRelativePath(
            _normalizedPrefix,
            sourceId,
            resolvedExecutionId,
            documentId);
        string content = await DownloadTextAsync(blobPath, cancellationToken).ConfigureAwait(false);

        return new ReadNormalizedDocumentResponse
        {
            SourceId = sourceId,
            ExecutionId = resolvedExecutionId,
            DocumentId = documentId,
            StoragePath = blobPath,
            Content = NormalizedDocumentAgentView.Compact(content)
        };
    }

    private async Task<string> ResolveExecutionIdForListAsync(
        string sourceId,
        string requestedExecutionId,
        CancellationToken cancellationToken)
    {
        string normalizedRequested = NormalizedExecutionIdResolver.Normalize(requestedExecutionId);
        string manifestPath = NormalizedDocumentPathBuilder.BuildManifestRelativePath(
            _normalizedPrefix,
            sourceId,
            normalizedRequested);

        if (await BlobExistsAsync(manifestPath, cancellationToken).ConfigureAwait(false))
        {
            return normalizedRequested;
        }

        string? resolved = await TryResolveExecutionIdAsync(
                sourceId,
                normalizedRequested,
                documentId: null,
                cancellationToken)
            .ConfigureAwait(false);

        if (resolved is not null)
        {
            _logger.LogWarning(
                "Resolved mistyped executionId {RequestedExecutionId} to {ResolvedExecutionId} for source {SourceId} during list_normalized_documents.",
                requestedExecutionId,
                resolved,
                sourceId);
            return resolved;
        }

        throw new FileNotFoundException(
            $"Normalized manifest not found for source '{sourceId}' and execution '{requestedExecutionId}'.");
    }

    private async Task<string> ResolveExecutionIdForReadAsync(
        string sourceId,
        string requestedExecutionId,
        string documentId,
        CancellationToken cancellationToken)
    {
        string normalizedRequested = NormalizedExecutionIdResolver.Normalize(requestedExecutionId);
        string blobPath = NormalizedDocumentPathBuilder.BuildDocumentRelativePath(
            _normalizedPrefix,
            sourceId,
            normalizedRequested,
            documentId);

        if (await BlobExistsAsync(blobPath, cancellationToken).ConfigureAwait(false))
        {
            return normalizedRequested;
        }

        string? resolved = await TryResolveExecutionIdAsync(
                sourceId,
                normalizedRequested,
                documentId,
                cancellationToken)
            .ConfigureAwait(false);

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

        throw new FileNotFoundException(
            $"Normalized blob not found: {blobPath}");
    }

    private async Task<string?> TryResolveExecutionIdAsync(
        string sourceId,
        string requestedExecutionId,
        string? documentId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> availableExecutionIds = await ListExecutionIdsAsync(sourceId, cancellationToken)
            .ConfigureAwait(false);

        string? closest = NormalizedExecutionIdResolver.FindClosestMatch(requestedExecutionId, availableExecutionIds);
        if (closest is not null)
        {
            return NormalizedExecutionIdResolver.Normalize(closest);
        }

        if (string.IsNullOrWhiteSpace(documentId))
        {
            return null;
        }

        string documentSuffix = $"/documents/{NormalizedDocumentPathBuilder.ToStorageFileName(documentId)}";
        string prefix = $"{_normalizedPrefix.TrimEnd('/')}/{sourceId.Trim()}/";

        await foreach (Azure.Page<BlobItem> page in _containerClient
            .GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix: prefix, cancellationToken: cancellationToken)
            .AsPages())
        {
            foreach (BlobItem blob in page.Values)
            {
                if (!blob.Name.EndsWith(documentSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (NormalizedExecutionIdResolver.TryParseExecutionIdFromBlobPath(
                        blob.Name,
                        _normalizedPrefix,
                        sourceId,
                        out string executionId))
                {
                    return executionId;
                }
            }
        }

        return null;
    }

    private async Task<IReadOnlyList<string>> ListExecutionIdsAsync(
        string sourceId,
        CancellationToken cancellationToken)
    {
        var executionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string prefix = $"{_normalizedPrefix.TrimEnd('/')}/{sourceId.Trim()}/";

        await foreach (Azure.Page<BlobHierarchyItem> page in _containerClient
            .GetBlobsByHierarchyAsync(BlobTraits.None, BlobStates.None, prefix: prefix, delimiter: "/", cancellationToken: cancellationToken)
            .AsPages())
        {
            foreach (BlobHierarchyItem item in page.Values)
            {
                if (!item.IsPrefix || string.IsNullOrWhiteSpace(item.Prefix))
                {
                    continue;
                }

                string trimmedPrefix = item.Prefix.TrimEnd('/');
                int lastSlash = trimmedPrefix.LastIndexOf('/');
                if (lastSlash < 0 || lastSlash == trimmedPrefix.Length - 1)
                {
                    continue;
                }

                string candidate = trimmedPrefix[(lastSlash + 1)..];
                if (NormalizedExecutionIdResolver.IsValidFormat(candidate))
                {
                    executionIds.Add(NormalizedExecutionIdResolver.Normalize(candidate));
                }
            }
        }

        return executionIds.ToList();
    }

    private async Task<bool> BlobExistsAsync(string blobPath, CancellationToken cancellationToken)
    {
        BlobClient blobClient = _containerClient.GetBlobClient(blobPath);
        Azure.Response<bool> exists = await blobClient.ExistsAsync(cancellationToken).ConfigureAwait(false);
        return exists.Value;
    }

    private async Task UploadTextAsync(string blobPath, string content, CancellationToken cancellationToken)
    {
        BlobClient blobClient = _containerClient.GetBlobClient(blobPath);
        byte[] payload = Encoding.UTF8.GetBytes(content);
        await using var stream = new MemoryStream(payload);
        await blobClient.UploadAsync(
            stream,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> DownloadTextAsync(string blobPath, CancellationToken cancellationToken)
    {
        BlobClient blobClient = _containerClient.GetBlobClient(blobPath);
        try
        {
            Response<BlobDownloadResult> download = await blobClient.DownloadContentAsync(cancellationToken)
                .ConfigureAwait(false);
            return download.Value.Content.ToString();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new FileNotFoundException($"Normalized blob not found: {blobPath}", ex);
        }
    }

    private static BlobServiceClient CreateBlobServiceClient(AzureBlobStorageOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new BlobServiceClient(options.ConnectionString);
        }

        if (!string.IsNullOrWhiteSpace(options.BlobServiceUri))
        {
            return new BlobServiceClient(new Uri(options.BlobServiceUri), new DefaultAzureCredential());
        }

        throw new InvalidOperationException(
            "Azure Blob Storage is not configured. Set AzureStorage:ConnectionString, AzureStorage:BlobServiceUri, or AZURE_STORAGE_BLOB_SERVICE_URI.");
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
