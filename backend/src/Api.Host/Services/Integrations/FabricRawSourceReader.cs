using Azure;
using CohereRndKnowledgeMining.Api.Host.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CohereRndKnowledgeMining.Api.Host.Services.Integrations;

public sealed class FabricRawSourceReader : IFabricRawSourceReader
{
    private readonly FabricLakehouseClient _client;
    private readonly string _evidenceRoot;
    private readonly ILogger<FabricRawSourceReader> _logger;

    public FabricRawSourceReader(
        FabricLakehouseClient client,
        IOptions<FabricLakehouseOptions> options,
        ILogger<FabricRawSourceReader> logger)
    {
        _client = client;
        _evidenceRoot = options.Value.EvidenceRoot.TrimEnd('/');
        _logger = logger;
    }

    public async Task<IReadOnlyList<RawKnowledgeItem>> ReadAsync(string sourceId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        var sourcePrefix = $"/{_evidenceRoot}/cases/{sourceId}/ingest/";
        _logger.LogInformation("Reading raw R&D knowledge from Fabric: prefix={SourcePrefix}", sourcePrefix);

        IReadOnlyList<string> allFiles;
        try
        {
            allFiles = await _client.ListFilesAsync("", cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Filesystem root not found (404)");
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list filesystem root: {ErrorMessage}", ex.Message);
            throw;
        }

        if (allFiles.Count == 0)
        {
            _logger.LogInformation("No files found in filesystem root");
            return [];
        }

        var files = allFiles
            .Where(p => p.Contains(sourcePrefix, StringComparison.OrdinalIgnoreCase)
                     && !p.EndsWith('/'))
            .ToList();

        if (files.Count == 0)
        {
            _logger.LogInformation("No files found with prefix: {SourcePrefix}", sourcePrefix);
            return [];
        }

        _logger.LogInformation("Found {Count} files matching source {SourceId} (filtered from {Total} total)", files.Count, sourceId, allFiles.Count);

        var items = new List<RawKnowledgeItem>(files.Count);
        for (int i = 0; i < files.Count; i++)
        {
            var fileName = files[i][(files[i].LastIndexOf('/') + 1)..];
            var relativePath = BuildRelativePath(sourceId, fileName);

            string content;
            try
            {
                content = await _client.ReadFileAsync(relativePath, cancellationToken).ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                _logger.LogWarning("File not found during read: {RelativePath}", relativePath);
                continue;
            }

            items.Add(new RawKnowledgeItem
            {
                ItemId = $"{sourceId}-{i + 1:D3}",
                Title = fileName,
                SourceType = RawSourceTypeInference.InferSourceType(fileName),
                Content = content.Trim(),
                SourcePath = relativePath
            });
        }

        _logger.LogInformation("Read {Count} raw items from source {SourceId}", items.Count, sourceId);

        return items;
    }

    private string BuildRelativePath(string sourceId, string fileName) =>
        $"{_evidenceRoot}/cases/{sourceId}/ingest/{fileName}";
}
