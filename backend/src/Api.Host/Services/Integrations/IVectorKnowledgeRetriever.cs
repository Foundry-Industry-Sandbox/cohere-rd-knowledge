using CohereRndKnowledgeMining.Api.Host.Services;
using RndKnowledgeMining.Mcp.Adapters;

namespace CohereRndKnowledgeMining.Api.Host.Services.Integrations;

/// <summary>
/// Retrieves grounding passages from the Vector DB for Block 2 Search &amp; Chat
/// (Cohere Embed query -> Vector DB -> Cohere Rerank -> Top-N).
/// </summary>
public interface IVectorKnowledgeRetriever
{
    Task<IReadOnlyList<RetrievedPassage>> RetrieveAsync(
        string sessionId,
        string query,
        int topN,
        CancellationToken cancellationToken);
}

/// <summary>
/// API-side retriever that delegates to the shared <see cref="IKnowledgeSearchService"/>
/// used by MCP <c>search_rd_knowledge</c>.
/// </summary>
public sealed class AzureVectorKnowledgeRetriever : IVectorKnowledgeRetriever
{
    private readonly IKnowledgeSearchService _knowledgeSearchService;

    public AzureVectorKnowledgeRetriever(IKnowledgeSearchService knowledgeSearchService)
    {
        _knowledgeSearchService = knowledgeSearchService;
    }

    public async Task<IReadOnlyList<RetrievedPassage>> RetrieveAsync(
        string sessionId,
        string query,
        int topN,
        CancellationToken cancellationToken)
    {
        var response = await _knowledgeSearchService
            .SearchAsync(sessionId, query, topN, cancellationToken)
            .ConfigureAwait(false);

        return response.Matches
            .Select(match => new RetrievedPassage
            {
                PassageId = match.PassageId,
                Content = FormatPassageContent(match.Title, match.Snippet),
                Citation = match.EntityId,
                Score = match.Score
            })
            .ToArray();
    }

    private static string FormatPassageContent(string title, string snippet)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return snippet;
        }

        if (string.IsNullOrWhiteSpace(snippet))
        {
            return title;
        }

        return $"{title.Trim()}\n{snippet.Trim()}";
    }
}
