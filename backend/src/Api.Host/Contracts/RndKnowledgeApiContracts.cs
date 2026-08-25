using System.Text.Json.Serialization;

namespace CohereRndKnowledgeMining.Api.Host.Contracts;

// ---- Block 1: Ingestion ----

public sealed class StartIngestionRequest
{
    /// <summary>Identifier of the raw R&D knowledge batch to ingest.</summary>
    public required string SourceId { get; init; }

    /// <summary>Optional caller-provided execution id; generated when omitted.</summary>
    public string? ExecutionId { get; init; }
}

// ---- Shared: Enums ----

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkflowStatus
{
    Pending,
    Running,
    AwaitingHumanApproval,
    Completed,
    Failed
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IngestionStage
{
    Pending,
    IngestionTranslation,
    MetadataLinking,
    HumanApproval,
    Completed,
    Failed
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QueryStage
{
    Pending,
    ChatActive,
    CurationRunning,
    AwaitingComplianceReview,
    Completed,
    Failed
}

// ---- Shared: Supporting Types ----

public sealed class StudySummaryResponse
{
    public string StudyId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Compound { get; init; } = string.Empty;

    public string Phase { get; init; } = string.Empty;

    public string PrimaryEndpoint { get; init; } = string.Empty;

    public IReadOnlyList<string> SourceSystems { get; init; } = [];
}

public sealed class PortfolioScenarioResponse
{
    public string ScenarioId { get; init; } = string.Empty;

    public string Block { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string StudyId { get; init; } = string.Empty;

    public string SourceId { get; init; } = string.Empty;

    public string? SampleQuestion { get; init; }

    public string OutcomeHint { get; init; } = string.Empty;

    public required StudySummaryResponse Study { get; init; }

    public string? LegacyScenarioId { get; init; }

    public string? CaseFolder { get; init; }

    public string? FinalOutcome { get; init; }
}

public sealed class HumanDecisionRecordResponse
{
    public bool Approved { get; init; }

    public string? Notes { get; init; }

    public DateTimeOffset DecidedAt { get; init; }
}

// ---- Block 1: Legacy (kept for backward compat) ----

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

// ---- Block 2: Query (Search & Chat + Curate) ----

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

// ---- Block 1: Study Documents ----

public sealed class StudyDocumentsResponse
{
    public string StudyId { get; init; } = string.Empty;

    public IReadOnlyList<KnowledgeSourceResponse> Sources { get; init; } = [];
}

public sealed class KnowledgeSourceResponse
{
    public string SourceId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string SourceType { get; init; } = string.Empty;

    public string Format { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;
}

// ---- Vector DB Summary ----

public sealed class VectorDbStoreSummaryResponse
{
    public int TotalStudies { get; init; }

    public int TotalDocuments { get; init; }

    public int TotalEntities { get; init; }

    public int TotalLinks { get; init; }

    public DateTimeOffset? LastIngestionAt { get; init; }

    public string? LastIngestedStudyId { get; init; }
}

public sealed class VectorDbClearDataResponse
{
    public required long DeletedCount { get; init; }
}

// ---- Query session reset ----

public sealed class QuerySessionsResetResponse
{
    public required int QuerySessionsCleared { get; init; }

    public required int CurateExecutionsCleared { get; init; }
}

// ---- Shared ----

public sealed class WorkflowApprovalRequest
{
    public required bool Approved { get; init; }

    public string? ReviewerComment { get; init; }
}

public sealed class ProblemDetailsResponse
{
    public required string Title { get; init; }

    public required string Detail { get; init; }
}
