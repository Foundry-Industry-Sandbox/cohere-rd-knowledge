namespace RndKnowledgeMining.Governance.Mcp;

internal sealed class McpRogueToolCallTracker
{
    private readonly Dictionary<string, Queue<string>> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public bool ShouldBlock(
        AgentRole role,
        string? caseId,
        string? executionId,
        string toolName,
        RoguePolicyConfig config)
    {
        string key = BuildKey(role, caseId, executionId);

        lock (_sync)
        {
            if (!_windows.TryGetValue(key, out Queue<string>? window))
            {
                window = new Queue<string>();
                _windows[key] = window;
            }

            window.Enqueue(toolName);
            while (window.Count > config.WindowSize)
            {
                window.Dequeue();
            }

            int riskyCount = window.Count(call =>
                string.Equals(call, config.RiskyTool, StringComparison.Ordinal));

            return riskyCount >= config.TriggerCount
                && string.Equals(toolName, config.RiskyTool, StringComparison.Ordinal);
        }
    }

    private static string BuildKey(AgentRole role, string? caseId, string? executionId) =>
        $"{role}:{caseId ?? "unknown"}:{executionId ?? "unknown"}";
}
