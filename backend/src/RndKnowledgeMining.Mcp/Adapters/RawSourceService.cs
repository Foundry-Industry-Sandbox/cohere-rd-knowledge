using Azure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RndKnowledgeMining.Mcp.Models;
using RndKnowledgeMining.Mcp.Options;

namespace RndKnowledgeMining.Mcp.Adapters;

/// <summary>
/// Lists and reads raw R&amp;D documents from Microsoft Fabric OneLake scoped to a sourceId.
/// Documents live under <c>{EvidenceRoot}/cases/{sourceId}/ingest/</c> (same layout as local dataset-seed).
/// </summary>
public sealed class FabricRawSourceService : IRawSourceService
{
    private readonly FabricLakehouseClient _client;
    private readonly string _evidenceRoot;
    private readonly ILogger<FabricRawSourceService> _logger;

    public FabricRawSourceService(
        FabricLakehouseClient client,
        IOptions<FabricLakehouseOptions> options,
        ILogger<FabricRawSourceService> logger)
    {
        _client = client;
        _evidenceRoot = options.Value.EvidenceRoot.Trim('/');
        _logger = logger;
    }

    public async Task<ListRawDocumentsResponse> ListAsync(string sourceId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        var files = await ListSourceFilesAsync(sourceId, cancellationToken).ConfigureAwait(false);

        var documents = files
            .Select(file => BuildSummary(sourceId, file))
            .ToList();

        _logger.LogInformation(
            "Listed {Count} raw documents for source {SourceId}",
            documents.Count,
            sourceId);

        return new ListRawDocumentsResponse
        {
            SourceId = sourceId,
            Documents = documents
        };
    }

    public async Task<ReadRawDocumentResponse> ReadAsync(string sourceId, string fileName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var relativePath = BuildRelativePath(sourceId, fileName);

        string content;
        try
        {
            content = await _client.ReadFileAsync(relativePath, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            throw new FileNotFoundException(
                $"Fabric file '{relativePath}' was not found for source '{sourceId}'.");
        }

        return new ReadRawDocumentResponse
        {
            Title = fileName,
            SourceType = RawSourceTypeInference.InferSourceType(fileName),
            SourcePath = relativePath,
            Content = RawDocumentContentPreparer.PrepareForAgent(fileName, content.Trim())
        };
    }

    private async Task<IReadOnlyList<string>> ListSourceFilesAsync(string sourceId, CancellationToken cancellationToken)
    {
        var ingestDirectory = $"{_evidenceRoot}/cases/{sourceId}/ingest";

        try
        {
            var files = await _client.ListFilesAsync(ingestDirectory, cancellationToken).ConfigureAwait(false);
            return files
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning(
                "Ingest directory not found (404) for source {SourceId} at {IngestDirectory}",
                sourceId,
                ingestDirectory);
            return [];
        }
    }

    private string BuildRelativePath(string sourceId, string fileName) =>
        $"{_evidenceRoot}/cases/{sourceId}/ingest/{fileName}";

    private RawDocumentSummary BuildSummary(string sourceId, string fileName) =>
        new()
        {
            FileName = fileName,
            SourceType = RawSourceTypeInference.InferSourceType(fileName),
            SourcePath = BuildRelativePath(sourceId, fileName)
        };
}
