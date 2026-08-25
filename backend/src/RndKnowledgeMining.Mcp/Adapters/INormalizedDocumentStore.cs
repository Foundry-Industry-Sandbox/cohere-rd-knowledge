using RndKnowledgeMining.Mcp.Models;

namespace RndKnowledgeMining.Mcp.Adapters;

public interface INormalizedDocumentStore
{
    Task<string> PersistIngestionHandoffAsync(
        string sourceId,
        string executionId,
        string ingestionPayloadJson,
        CancellationToken cancellationToken = default);

    Task<ListNormalizedDocumentsResponse> ListAsync(
        string sourceId,
        string executionId,
        CancellationToken cancellationToken = default);

    Task<ReadNormalizedDocumentResponse> ReadAsync(
        string sourceId,
        string executionId,
        string documentId,
        CancellationToken cancellationToken = default);
}
