using System.Text.Json;
using AgentGovernance;
using AgentGovernance.Integration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RndKnowledgeMining.Governance.Audit;

namespace RndKnowledgeMining.Governance.Mcp;

public sealed class McpToolGovernanceDecision
{
    public required bool Allowed { get; init; }

    public required string Reason { get; init; }
}

public sealed class McpToolGovernanceCoordinator
{
    private readonly FoundryAgentGovernanceBootstrap _bootstrap;
    private readonly IAgentGovernanceAuditStore _auditStore;
    private readonly McpRogueToolCallTracker _rogueTracker = new();
    private readonly GovernanceSettings _options;
    private readonly ILogger<McpToolGovernanceCoordinator>? _logger;

    public McpToolGovernanceCoordinator(
        FoundryAgentGovernanceBootstrap bootstrap,
        IAgentGovernanceAuditStore auditStore,
        IOptions<GovernanceSettings> options,
        ILogger<McpToolGovernanceCoordinator>? logger = null)
    {
        _bootstrap = bootstrap;
        _auditStore = auditStore;
        _options = options.Value;
        _logger = logger;
    }

    public McpToolGovernanceDecision EvaluateToolCall(
        AgentRole role,
        string toolName,
        IDictionary<string, JsonElement>? arguments)
    {
        if (!_options.EnableMcpToolGovernance)
        {
            return AllowedDecision("MCP governance disabled.");
        }

        string agentId = AgentIdentityCatalog.ToMeshAgentId(role);
        string? caseId = TryGetStringArgument(arguments, "caseId")
            ?? TryGetStringArgument(arguments, "sessionId");
        string? executionId = TryGetStringArgument(arguments, "executionId");
        Dictionary<string, object> evaluationArgs = ConvertArguments(arguments);

        GovernanceKernel kernel = _bootstrap.GetKernel(role);
        ToolCallResult policyResult = kernel.EvaluateToolCall(agentId, toolName, evaluationArgs);
        if (!policyResult.Allowed)
        {
            string reason = policyResult.Reason ?? $"Tool '{toolName}' is denied by governance policy.";
            AppendAudit(agentId, "ToolCallBlocked", "deny", caseId, executionId, reason);
            _logger?.LogWarning(
                "MCP governance blocked {ToolName} for {AgentRole}: {Reason}",
                toolName,
                role,
                reason);

            return new McpToolGovernanceDecision
            {
                Allowed = false,
                Reason = reason
            };
        }

        RoguePolicyConfig rogueConfig = _bootstrap.GetRogueConfig(role);
        if (_rogueTracker.ShouldBlock(role, caseId, executionId, toolName, rogueConfig))
        {
            string reason =
                $"Governance blocked risky tool '{toolName}' after repeated calls in the sliding window.";
            AppendAudit(agentId, "RogueToolCallBlocked", "deny", caseId, executionId, reason);
            _logger?.LogWarning(
                "MCP rogue detection blocked {ToolName} for {AgentRole}.",
                toolName,
                role);

            return new McpToolGovernanceDecision
            {
                Allowed = false,
                Reason = reason
            };
        }

        AppendAudit(agentId, "ToolCallAllowed", "allow", caseId, executionId, toolName);
        return AllowedDecision(toolName);
    }

    private void AppendAudit(
        string agentId,
        string action,
        string decision,
        string? caseId,
        string? executionId,
        string eventType)
    {
        _auditStore.Append(new AgentGovernanceAuditRecord(
            Seq: 0,
            TimestampUtc: DateTimeOffset.UtcNow,
            AgentId: agentId,
            Action: action,
            Decision: decision,
            PreviousHash: string.Empty,
            Hash: string.Empty,
            CaseId: caseId,
            ExecutionId: executionId,
            EventType: eventType));
    }

    private static McpToolGovernanceDecision AllowedDecision(string reason) =>
        new()
        {
            Allowed = true,
            Reason = reason
        };

    private static string? TryGetStringArgument(
        IDictionary<string, JsonElement>? arguments,
        string name)
    {
        if (arguments is null || !arguments.TryGetValue(name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => value.GetRawText()
        };
    }

    private static Dictionary<string, object> ConvertArguments(
        IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null)
        {
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, object> converted = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, JsonElement value) in arguments)
        {
            converted[key] = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => string.Empty,
                _ => value.GetRawText()
            };
        }

        return converted;
    }
}
