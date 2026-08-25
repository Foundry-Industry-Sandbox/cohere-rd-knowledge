using CohereRndKnowledgeMining.Api.Host.Options;
using CohereRndKnowledgeMining.Api.Host.Services.Integrations;
using Microsoft.Extensions.Options;

namespace CohereRndKnowledgeMining.Api.Host.Services;

/// <summary>
/// Loads all raw documents for a Block 1 source before the ingestion agent runs.
/// </summary>
public sealed class IngestionSourceDocumentLoader
{
    private readonly DataSourceMode _dataSourceMode;
    private readonly DatasetOptions _datasetOptions;
    private readonly IFabricRawSourceReader? _fabricReader;
    private readonly ILogger<IngestionSourceDocumentLoader> _logger;

    public IngestionSourceDocumentLoader(
        IOptions<DataSourceOptions> dataSourceOptions,
        IOptions<DatasetOptions> datasetOptions,
        ILogger<IngestionSourceDocumentLoader> logger,
        IFabricRawSourceReader? fabricReader = null)
    {
        _dataSourceMode = dataSourceOptions.Value.Mode;
        _datasetOptions = datasetOptions.Value;
        _fabricReader = fabricReader;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RawKnowledgeItem>> LoadAsync(
        string sourceId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        if (_dataSourceMode == DataSourceMode.Fabric)
        {
            if (_fabricReader is null)
            {
                throw new InvalidOperationException(
                    "Fabric raw source reader is not configured for Fabric mode.");
            }

            return await _fabricReader.ReadAsync(sourceId, cancellationToken).ConfigureAwait(false);
        }

        return LoadLocal(sourceId);
    }

    private IReadOnlyList<RawKnowledgeItem> LoadLocal(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(_datasetOptions.RootPath))
        {
            throw new InvalidOperationException("Dataset:RootPath is required for Local mode.");
        }

        string ingestDir = Path.Combine(_datasetOptions.RootPath, "cases", sourceId, "ingest");
        if (!Directory.Exists(ingestDir))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {ingestDir}");
        }

        var items = Directory.EnumerateFiles(ingestDir)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .Select((filePath, index) =>
            {
                string fileName = Path.GetFileName(filePath);
                return new RawKnowledgeItem
                {
                    ItemId = $"{sourceId}-{index + 1:D3}",
                    Title = fileName,
                    SourceType = RawSourceTypeInference.InferSourceType(fileName),
                    Content = File.ReadAllText(filePath).Trim(),
                    SourcePath = filePath
                };
            })
            .ToList();

        _logger.LogInformation(
            "Loaded {Count} local raw documents for source {SourceId}.",
            items.Count,
            sourceId);

        return items;
    }
}
