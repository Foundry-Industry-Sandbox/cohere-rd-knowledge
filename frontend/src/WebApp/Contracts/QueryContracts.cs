using Cohere.AgenticRDKnowledge.Shared.Contracts;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Agents;

namespace Cohere.AgenticRDKnowledge.Shared.Contracts.Query;

public sealed record ChatMessage(
    string Role,
    string Content,
    IReadOnlyList<Citation>? Citations,
    string? LineageSummary,
    IReadOnlyList<RetrievalTraceEvent>? RetrievalTrace,
    DateTimeOffset Timestamp);

public sealed record SendChatMessageRequest(
    string Question,
    string? StudyScope);

public sealed record QuerySessionState(
    string SessionId,
    string? StudyScope,
    IReadOnlyList<ChatMessage> Messages,
    bool IsChatRunning,
    string? CurationExecutionId,
    WorkflowStatus CurationStatus,
    QueryStage CurrentStage,
    string StatusMessage,
    CurationComplianceResult? CurationCompliance,
    HumanDecisionRecord? HumanDecision,
    IReadOnlyList<string> AllowedActions);

public sealed record CurationWorkflowProgress(
    string ExecutionId,
    string SessionId,
    WorkflowStatus Status,
    QueryStage CurrentStage,
    string StatusMessage,
    CurationComplianceResult? CurationCompliance,
    HumanDecisionRecord? HumanDecision,
    IReadOnlyList<string> AllowedActions);

public sealed record SubmitQueryDecisionRequest(
    bool Approved,
    string? Notes);

public sealed record SubmitQueryDecisionResponse(
    string ExecutionId,
    WorkflowStatus Status);

public sealed record QuerySessionsResetResult(
    int QuerySessionsCleared,
    int CurateExecutionsCleared);
