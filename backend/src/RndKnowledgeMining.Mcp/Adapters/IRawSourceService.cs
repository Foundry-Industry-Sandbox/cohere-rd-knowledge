using RndKnowledgeMining.Mcp.Models;

namespace RndKnowledgeMining.Mcp.Adapters;

public interface IRawSourceService
{
    Task<ListRawDocumentsResponse> ListAsync(string sourceId, CancellationToken cancellationToken);

    Task<ReadRawDocumentResponse> ReadAsync(string sourceId, string fileName, CancellationToken cancellationToken);
}
