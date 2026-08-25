using Cohere.AgenticRDKnowledge.Shared.Contracts;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Agents;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Studies;

namespace Cohere.AgenticRDKnowledge.Shared.Contracts.Ingestion;

public sealed record StartIngestionWorkflowRequest(string StudyId);

public sealed record StartIngestionWorkflowResponse(
    string ExecutionId,
    string StudyId,
    WorkflowStatus Status);

public sealed record IngestionWorkflowProgress(
    string ExecutionId,
    string StudyId,
    WorkflowStatus Status,
    IngestionStage CurrentStage,
    string StatusMessage,
    StudySummary? Study,
    IngestionTranslationResult? IngestionTranslation,
    MetadataLinkingResult? MetadataLinking,
    IReadOnlyList<RetrievalTraceEvent>? RetrievalTrace,
    HumanDecisionRecord? HumanDecision,
    IReadOnlyList<string> AllowedActions,
    string? FailureReason = null);

public sealed record SubmitIngestionDecisionRequest(
    bool Approved,
    string? Notes);

public sealed record SubmitIngestionDecisionResponse(
    string ExecutionId,
    WorkflowStatus Status);
