namespace Cohere.AgenticRDKnowledge.Shared.Contracts.Backend;

public sealed class StartIngestionRequest
{
    public required string SourceId { get; init; }

    public string? ExecutionId { get; init; }
}

public sealed class IngestionWorkflowStatusResponse
{
    public required string ExecutionId { get; init; }

    public required string SourceId { get; init; }

    public required string Status { get; init; }

    public required IngestionAgentOutputsResponse AgentOutputs { get; init; }

    public string? FailureReason { get; init; }

    public required DateTimeOffset LastUpdatedUtc { get; init; }
}

public sealed class IngestionAgentOutputsResponse
{
    public string? IngestionTranslation { get; init; }

    public string? MetadataLinking { get; init; }
}

public sealed class AskRequest
{
    public required string SessionId { get; init; }

    public required string Question { get; init; }
}

public sealed class ChatAnswerResponse
{
    public required string SessionId { get; init; }

    public required string Question { get; init; }

    public required string Answer { get; init; }

    public required IReadOnlyList<string> Citations { get; init; }

    public required int TurnCount { get; init; }

    public required bool CurateEnabled { get; init; }
}

public sealed class StartCurateRequest
{
    public required string SessionId { get; init; }

    public string? ExecutionId { get; init; }
}

public sealed class CurateWorkflowStatusResponse
{
    public required string ExecutionId { get; init; }

    public required string SessionId { get; init; }

    public required string Status { get; init; }

    public string? CurationOutput { get; init; }

    public string? FailureReason { get; init; }

    public required DateTimeOffset LastUpdatedUtc { get; init; }
}

public sealed class WorkflowApprovalRequest
{
    public required bool Approved { get; init; }

    public string? ReviewerComment { get; init; }
}

public sealed class VectorDbStoreSummaryResponse
{
    public int TotalStudies { get; init; }

    public int TotalDocuments { get; init; }

    public int TotalEntities { get; init; }

    public int TotalLinks { get; init; }

    public DateTimeOffset? LastIngestionAt { get; init; }

    public string? LastIngestedStudyId { get; init; }
}
