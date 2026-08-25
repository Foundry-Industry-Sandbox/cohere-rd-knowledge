using Microsoft.Extensions.Logging;
using RndKnowledgeMining.Mcp.Models;

namespace RndKnowledgeMining.Mcp.Adapters;

/// <summary>
/// Lists and reads raw R&amp;D documents from the local filesystem scoped to a sourceId.
/// Documents live under <c>{RootPath}/cases/{sourceId}/ingest/</c>.
/// </summary>
public sealed class LocalRawSourceService : IRawSourceService
{
    private readonly string _rootPath;
    private readonly ILogger<LocalRawSourceService> _logger;

    public LocalRawSourceService(string rootPath, ILogger<LocalRawSourceService> logger)
    {
        _rootPath = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _logger = logger;
    }

    public Task<ListRawDocumentsResponse> ListAsync(string sourceId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        var ingestDir = Path.Combine(_rootPath, "cases", sourceId, "ingest");

        if (!Directory.Exists(ingestDir))
        {
            _logger.LogInformation("Source directory not found: {IngestDir}", ingestDir);
            return Task.FromResult(new ListRawDocumentsResponse
            {
                SourceId = sourceId,
                Documents = []
            });
        }

        var files = Directory.EnumerateFiles(ingestDir)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();

        var documents = files
            .Select(file => BuildSummary(file))
            .ToList();

        _logger.LogInformation(
            "Listed {Count} raw documents for source {SourceId}",
            documents.Count,
            sourceId);

        return Task.FromResult(new ListRawDocumentsResponse
        {
            SourceId = sourceId,
            Documents = documents
        });
    }

    public Task<ReadRawDocumentResponse> ReadAsync(string sourceId, string fileName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var ingestDir = Path.Combine(_rootPath, "cases", sourceId, "ingest");
        if (!Directory.Exists(ingestDir))
        {
            throw new DirectoryNotFoundException(
                $"Source directory not found: {ingestDir}");
        }

        var filePath = Path.Combine(ingestDir, fileName);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                $"File '{fileName}' not found in source '{sourceId}'.");
        }

        var sourceType = RawSourceTypeInference.InferSourceType(fileName);
        string content = RawDocumentContentPreparer.PrepareForAgent(fileName, File.ReadAllText(filePath).Trim());

        return Task.FromResult(new ReadRawDocumentResponse
        {
            Title = fileName,
            SourceType = sourceType,
            SourcePath = filePath,
            Content = content
        });
    }

    private static RawDocumentSummary BuildSummary(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return new RawDocumentSummary
        {
            FileName = fileName,
            SourceType = RawSourceTypeInference.InferSourceType(fileName),
            SourcePath = filePath
        };
    }
}
