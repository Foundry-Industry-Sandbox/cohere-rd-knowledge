using Cohere.AgenticRDKnowledge.Shared.Contracts;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Ingestion;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Query;

namespace Cohere.AgenticRDKnowledge.WebApp.Services;

public sealed record KnowledgeSessionSummary(
    string SessionId,
    string Title,
    WorkflowBlock Block,
    WorkflowStatus Status,
    string? ExecutionId,
    int ChatMessageCount,
    DateTimeOffset OpenedAt);

public sealed class KnowledgeSession
{
    public required string SessionId { get; init; }
    public required WorkflowBlock Block { get; init; }
    public required string Title { get; set; }
    public required string StudyId { get; init; }
    public string? SourceId { get; init; }
    public string? ScenarioId { get; init; }
    public string? SampleQuestion { get; set; }
    public string? ExecutionId { get; set; }
    public int ChatMessageCount { get; set; }
    public WorkflowStatus Status { get; set; } = WorkflowStatus.Pending;
    public DateTimeOffset OpenedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class KnowledgeSessionStore
{
    private readonly Dictionary<string, KnowledgeSession> _ingestionSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, KnowledgeSession> _querySessions = new(StringComparer.OrdinalIgnoreCase);

    public KnowledgeSession OpenIngestionSession(
        string studyId,
        string title,
        string? scenarioId = null,
        string? sourceId = null)
    {
        var session = new KnowledgeSession
        {
            SessionId = studyId,
            Block = WorkflowBlock.Ingestion,
            Title = title,
            StudyId = studyId,
            SourceId = sourceId,
            ScenarioId = scenarioId,
            ExecutionId = null,
            ChatMessageCount = 0,
            Status = WorkflowStatus.Pending
        };
        _ingestionSessions[session.SessionId] = session;
        return session;
    }

    public KnowledgeSession OpenQuerySession(
        string sessionId,
        string title,
        string studyScope,
        string question,
        string? scenarioId = null)
    {
        var session = new KnowledgeSession
        {
            SessionId = sessionId,
            Block = WorkflowBlock.Query,
            Title = title,
            StudyId = studyScope,
            ScenarioId = scenarioId,
            SampleQuestion = question,
            ExecutionId = null,
            ChatMessageCount = 0,
            Status = WorkflowStatus.Pending
        };
        _querySessions[sessionId] = session;
        return session;
    }

    public KnowledgeSession? GetSession(string sessionId) =>
        _ingestionSessions.TryGetValue(sessionId, out var ingestionSession)
            ? ingestionSession
            : null;

    public KnowledgeSession? GetQuerySession(string sessionId) =>
        _querySessions.TryGetValue(sessionId, out var session) ? session : null;

    public KnowledgeSession? GetQuerySessionBySessionId(string sessionId) => GetQuerySession(sessionId);

    public void UpdateSession(KnowledgeSession session)
    {
        if (session.Block == WorkflowBlock.Ingestion)
        {
            _ingestionSessions[session.SessionId] = session;
        }
        else
        {
            _querySessions[session.SessionId] = session;
        }
    }

    public IReadOnlyList<KnowledgeSessionSummary> GetSummaries() =>
        _ingestionSessions.Values
            .Concat(_querySessions.Values)
            .OrderByDescending(s => s.OpenedAt)
            .Select(s => new KnowledgeSessionSummary(
                s.SessionId,
                s.Title,
                s.Block,
                s.Status,
                s.ExecutionId,
                s.ChatMessageCount,
                s.OpenedAt))
            .ToList();

    public void ClearAll()
    {
        _ingestionSessions.Clear();
        _querySessions.Clear();
    }
}
