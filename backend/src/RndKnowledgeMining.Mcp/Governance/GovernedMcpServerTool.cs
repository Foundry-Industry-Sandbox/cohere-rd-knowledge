using System.Text.Json;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RndKnowledgeMining.Governance;
using RndKnowledgeMining.Governance.Mcp;

namespace RndKnowledgeMining.Mcp.Governance;

public sealed class GovernedMcpServerTool : DelegatingMcpServerTool
{
    private readonly McpToolGovernanceCoordinator _coordinator;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GovernedMcpServerTool(
        McpServerTool innerTool,
        McpToolGovernanceCoordinator coordinator,
        IHttpContextAccessor httpContextAccessor)
        : base(innerTool)
    {
        _coordinator = coordinator;
        _httpContextAccessor = httpContextAccessor;
    }

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        GovernanceSettings settings = _httpContextAccessor.HttpContext?.RequestServices
            .GetService<IOptions<GovernanceSettings>>()?.Value
            ?? new GovernanceSettings();

        if (!settings.EnableMcpToolGovernance)
        {
            return await base.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }

        if (!TryResolveAgentRole(out AgentRole role))
        {
            return CreateBlockedResult($"Unable to resolve agent role from {AgentCatalog.AgentRoleHeaderName} header.");
        }

        string toolName = request.Params?.Name ?? ProtocolTool.Name;
        McpToolGovernanceDecision decision = _coordinator.EvaluateToolCall(
            role,
            toolName,
            request.Params?.Arguments);

        if (!decision.Allowed)
        {
            return CreateBlockedResult(decision.Reason);
        }

        return await base.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private bool TryResolveAgentRole(out AgentRole role)
    {
        role = default;
        HttpContext? httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return false;
        }

        if (httpContext.Items.TryGetValue(McpGovernanceHttpContextKeys.AgentRole, out object? stored)
            && stored is AgentRole storedRole)
        {
            role = storedRole;
            return true;
        }

        string? headerValue = httpContext.Request.Headers[AgentCatalog.AgentRoleHeaderName].FirstOrDefault();
        if (!AgentCatalog.TryResolveRoleFromHeader(headerValue, out role))
        {
            return false;
        }

        return AgentCatalog.IsRoleAllowedOnPath(role, httpContext.Request.Path.Value);
    }

    private static CallToolResult CreateBlockedResult(string message) =>
        new()
        {
            IsError = true,
            Content =
            [
                new TextContentBlock
                {
                    Text = message
                }
            ]
        };
}
