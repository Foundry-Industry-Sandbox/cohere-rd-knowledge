using System.ComponentModel;
using RndKnowledgeMining.Mcp.Adapters;
using RndKnowledgeMining.Mcp.Models;
using ModelContextProtocol.Server;

namespace RndKnowledgeMining.Mcp.Tools;

public sealed class RawSourceTools
{
    private readonly IRawSourceService _rawSourceService;

    public RawSourceTools(IRawSourceService rawSourceService)
    {
        _rawSourceService = rawSourceService;
    }

    [McpServerTool]
    [Description("Lists raw R&D documents for the given sourceId. Returns metadata only (fileName, sourceType, sourcePath); use read_raw_document to fetch content.")]
    public Task<ListRawDocumentsResponse> ListRawDocuments(
        [Description("Identifier of the ingestion source/batch (e.g. case-04-demo).")]
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        return _rawSourceService.ListAsync(sourceId, cancellationToken);
    }

    [McpServerTool]
    [Description("Reads a single raw R&D document by sourceId and fileName. JATS article XML is returned as compact pre-extracted JSON (metadata, abstract, section summaries); other files return text content.")]
    public Task<ReadRawDocumentResponse> ReadRawDocument(
        [Description("Identifier of the ingestion source/batch (e.g. case-04-demo).")]
        string sourceId,
        [Description("fileName returned by list_raw_documents (e.g. PMC13070087_article.xml).")]
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return _rawSourceService.ReadAsync(sourceId, fileName, cancellationToken);
    }

}
