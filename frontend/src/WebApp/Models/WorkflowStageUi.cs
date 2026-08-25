using Cohere.AgenticRDKnowledge.Shared.Contracts;

namespace Cohere.AgenticRDKnowledge.WebApp.Models;

public static class WorkflowStageUi
{
    public static string ToBusinessStatusLabel(WorkflowStatus status) => status switch
    {
        WorkflowStatus.Pending => "Pending",
        WorkflowStatus.Running => "In progress",
        WorkflowStatus.AwaitingHumanApproval => "Action required",
        WorkflowStatus.Completed => "Completed",
        WorkflowStatus.Failed => "Failed",
        _ => status.ToString()
    };

    public static string ToStatusBadgeClass(WorkflowStatus status) => status switch
    {
        WorkflowStatus.Completed => "status-success",
        WorkflowStatus.Failed => "status-danger",
        WorkflowStatus.AwaitingHumanApproval => "status-warning",
        WorkflowStatus.Running => "status-info",
        _ => "status-neutral"
    };

    public static string ToIngestionStageLabel(IngestionStage stage) => stage switch
    {
        IngestionStage.IngestionTranslation => "Ingestion & Translation",
        IngestionStage.MetadataLinking => "Metadata & Linking",
        IngestionStage.HumanApproval => "Knowledge Curator",
        IngestionStage.Completed => "Completed",
        IngestionStage.Failed => "Failed",
        _ => "Pending"
    };

    public static string ToQueryStageLabel(QueryStage stage) => stage switch
    {
        QueryStage.ChatActive => "Search & Chat",
        QueryStage.CurationRunning => "Curation & Compliance",
        QueryStage.AwaitingComplianceReview => "Compliance Reviewer",
        QueryStage.Completed => "Completed",
        QueryStage.Failed => "Failed",
        _ => "Pending"
    };
}
