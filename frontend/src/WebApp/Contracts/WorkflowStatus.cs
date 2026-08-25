using System.Text.Json.Serialization;

namespace Cohere.AgenticRDKnowledge.Shared.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkflowStatus
{
    Pending,
    Running,
    AwaitingHumanApproval,
    Completed,
    Failed
}

public enum WorkflowBlock
{
    Ingestion,
    Query
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
