using System.ComponentModel;
using RndKnowledgeMining.Mcp.Adapters;
using RndKnowledgeMining.Mcp.Models;
using ModelContextProtocol.Server;

namespace RndKnowledgeMining.Mcp.Tools;

public sealed class CurationComplianceTools
{
    private readonly IPolicySearchService _policySearchService;
    private readonly SensitiveContentScanner _sensitiveContentScanner;

    public CurationComplianceTools(
        IPolicySearchService policySearchService,
        SensitiveContentScanner sensitiveContentScanner)
    {
        _policySearchService = policySearchService;
        _sensitiveContentScanner = sensitiveContentScanner;
    }

    [McpServerTool]
    [Description("Retrieves relevant HLS trial, licensing, regional, or pharmacovigilance policies for curation review.")]
    public Task<GetRelevantPoliciesResponse> GetRelevantPolicies(
        [Description("Required natural-language query describing the policy area to retrieve.")]
        string query,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        return _policySearchService.GetRelevantPoliciesAsync(query, topK, cancellationToken);
    }

    [McpServerTool]
    [Description("Retrieves HLS policy entries by exact reference codes such as HLS-TRIAL-300 or HLS-LIC-200.")]
    public Task<GetRelevantPoliciesResponse> GetPoliciesByRefs(
        [Description("Policy reference codes to retrieve.")]
        string[] policyRefs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policyRefs);
        return _policySearchService.GetPoliciesByRefsAsync(policyRefs, cancellationToken);
    }

    [McpServerTool]
    [Description("Assesses chat or review text for PHI, PII, confidential partner material, or restricted regulatory content.")]
    public Task<FlagSensitiveContentResponse> FlagSensitiveContent(
        string sessionId,
        [Description("Chat or review text to scan for sensitive content.")]
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_sensitiveContentScanner.Scan(sessionId, text));
    }
}
