using RndKnowledgeMining.Mcp.Models;

namespace RndKnowledgeMining.Mcp.Adapters;

public sealed class AzurePolicySearchService : IPolicySearchService
{
    private readonly PolicyIndexAdapter _policyIndexAdapter;

    public AzurePolicySearchService(PolicyIndexAdapter policyIndexAdapter)
    {
        _policyIndexAdapter = policyIndexAdapter;
    }

    public Task<GetRelevantPoliciesResponse> GetRelevantPoliciesAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default) =>
        _policyIndexAdapter.GetRelevantPoliciesAsync(query, caseContext: null, topK, cancellationToken);

    public Task<GetRelevantPoliciesResponse> GetPoliciesByRefsAsync(
        IReadOnlyList<string> policyRefs,
        CancellationToken cancellationToken = default) =>
        _policyIndexAdapter.GetPoliciesByRefsAsync(policyRefs, cancellationToken);
}
