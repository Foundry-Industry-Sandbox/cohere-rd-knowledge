using RndKnowledgeMining.Mcp.Models;

namespace RndKnowledgeMining.Mcp.Adapters;

public interface IKnowledgeSearchService
{
    Task<SearchRdKnowledgeResponse> SearchAsync(
        string sessionId,
        string query,
        int topK,
        CancellationToken cancellationToken = default);

    Task<KnowledgeLineageResponse> GetLineageAsync(
        string sessionId,
        string passageId,
        CancellationToken cancellationToken = default);

    Task<IndexRdKnowledgeResponse> IndexAsync(
        string sessionId,
        string entityId,
        string entityType,
        string title,
        string chunkText,
        IReadOnlyList<string>? linkedEntities = null,
        string? lineageNarrative = null,
        string? passageId = null,
        CancellationToken cancellationToken = default);

    Task<IndexRdKnowledgeBatchResponse> IndexBatchAsync(
        string sessionId,
        IReadOnlyList<IndexRdKnowledgeBatchItem> items,
        CancellationToken cancellationToken = default);
}

public interface IPolicySearchService
{
    Task<GetRelevantPoliciesResponse> GetRelevantPoliciesAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default);

    Task<GetRelevantPoliciesResponse> GetPoliciesByRefsAsync(
        IReadOnlyList<string> policyRefs,
        CancellationToken cancellationToken = default);
}

public sealed class AzureKnowledgeSearchService : IKnowledgeSearchService
{
    private readonly KnowledgeIndexAdapter _knowledgeIndexAdapter;

    public AzureKnowledgeSearchService(KnowledgeIndexAdapter knowledgeIndexAdapter)
    {
        _knowledgeIndexAdapter = knowledgeIndexAdapter;
    }

    public Task<SearchRdKnowledgeResponse> SearchAsync(
        string sessionId,
        string query,
        int topK,
        CancellationToken cancellationToken = default) =>
        _knowledgeIndexAdapter.SearchAsync(sessionId, query, topK, cancellationToken);

    public Task<KnowledgeLineageResponse> GetLineageAsync(
        string sessionId,
        string passageId,
        CancellationToken cancellationToken = default) =>
        _knowledgeIndexAdapter.GetLineageAsync(sessionId, passageId, cancellationToken);

    public Task<IndexRdKnowledgeResponse> IndexAsync(
        string sessionId,
        string entityId,
        string entityType,
        string title,
        string chunkText,
        IReadOnlyList<string>? linkedEntities = null,
        string? lineageNarrative = null,
        string? passageId = null,
        CancellationToken cancellationToken = default) =>
        _knowledgeIndexAdapter.IndexAsync(
            sessionId,
            entityId,
            entityType,
            title,
            chunkText,
            linkedEntities,
            lineageNarrative,
            passageId,
            cancellationToken);

    public Task<IndexRdKnowledgeBatchResponse> IndexBatchAsync(
        string sessionId,
        IReadOnlyList<IndexRdKnowledgeBatchItem> items,
        CancellationToken cancellationToken = default) =>
        _knowledgeIndexAdapter.IndexBatchAsync(sessionId, items, cancellationToken);
}
