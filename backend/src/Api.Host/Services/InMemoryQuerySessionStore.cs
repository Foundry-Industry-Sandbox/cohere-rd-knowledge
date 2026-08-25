using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace CohereRndKnowledgeMining.Api.Host.Services;

/// <summary>
/// A single Search &amp; Chat turn within a query session (Block 2, Process 1).
/// </summary>
public sealed class ChatTurn
{
    public required string Question { get; init; }

    public required string Answer { get; init; }

    public IReadOnlyList<string> Citations { get; init; } = [];

    /// <summary>True when this turn contains grounded knowledge eligible for Curate.</summary>
    public bool IsGrounded { get; init; }

    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// An interactive Search &amp; Chat session. Holds the conversation thread and the accumulated
/// responses that feed the on-demand Curate process (Block 2, Process 2).
/// </summary>
public sealed class QueryChatSession
{
    public required string SessionId { get; init; }

    /// <summary>Full conversation history maintained across turns (this AIAgent build is thread-less).</summary>
    public List<ChatMessage> History { get; } = [];

    public List<ChatTurn> Turns { get; } = [];

    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class InMemoryQuerySessionStore
{
    private readonly ConcurrentDictionary<string, QueryChatSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, WorkflowExecution> _curateExecutions = new(StringComparer.OrdinalIgnoreCase);

    public QueryChatSession GetOrCreateSession(string sessionId)
    {
        return _sessions.GetOrAdd(sessionId, static id => new QueryChatSession { SessionId = id });
    }

    public QueryChatSession GetRequiredSession(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out QueryChatSession? session))
        {
            return session;
        }

        throw new KeyNotFoundException($"Query session '{sessionId}' was not found.");
    }

    public void SaveCurateExecution(WorkflowExecution execution) => _curateExecutions[execution.ExecutionId] = execution;

    public WorkflowExecution GetRequiredCurateExecution(string executionId)
    {
        if (_curateExecutions.TryGetValue(executionId, out WorkflowExecution? execution))
        {
            return execution;
        }

        throw new KeyNotFoundException($"Curate execution '{executionId}' was not found.");
    }

    public (int SessionsCleared, int CurateExecutionsCleared) ClearAll()
    {
        int sessions = _sessions.Count;
        int curate = _curateExecutions.Count;
        _sessions.Clear();
        _curateExecutions.Clear();
        return (sessions, curate);
    }
}
