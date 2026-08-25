using System.Collections.Concurrent;
using Cohere.AgenticRDKnowledge.Shared.Contracts;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Query;

namespace Cohere.AgenticRDKnowledge.WebApp.Services;

public sealed class QuerySessionCache
{
    private readonly ConcurrentDictionary<string, CachedQuerySession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public CachedQuerySession GetOrCreate(string sessionId, string? studyScope = null)
    {
        return _sessions.GetOrAdd(
            sessionId,
            _ => new CachedQuerySession { SessionId = sessionId, StudyScope = studyScope });
    }

    public CachedQuerySession? TryGet(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var session) ? session : null;

    public void SetCurationExecutionId(string sessionId, string curationExecutionId)
    {
        var session = GetOrCreate(sessionId);
        session.CurationExecutionId = curationExecutionId;
    }

    public void ClearAll() => _sessions.Clear();
}

public sealed class CachedQuerySession
{
    public required string SessionId { get; init; }

    public string? StudyScope { get; set; }

    public List<ChatMessage> Messages { get; } = [];

    public bool CurateEnabled { get; set; }

    public string? CurationExecutionId { get; set; }
}
