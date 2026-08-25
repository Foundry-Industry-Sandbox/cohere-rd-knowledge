using Cohere.AgenticRDKnowledge.Shared.Contracts;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Agents;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Backend;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Ingestion;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Query;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Studies;
using Cohere.AgenticRDKnowledge.Shared.Contracts.VectorDb;

namespace Cohere.AgenticRDKnowledge.WebApp.Services;

public static class BackendApiMapper
{
    public static IngestionWorkflowProgress ToIngestionProgress(
        IngestionWorkflowStatusResponse response,
        StudySummary? study = null)
    {
        var status = ParseWorkflowStatus(response.Status);
        var translation = AgentOutputParser.ParseIngestionTranslation(response.AgentOutputs.IngestionTranslation);
        var linking = AgentOutputParser.ParseMetadataLinking(response.AgentOutputs.MetadataLinking);
        var stage = DeriveIngestionStage(
            status,
            translation,
            linking,
            response.AgentOutputs.IngestionTranslation,
            response.AgentOutputs.MetadataLinking);

        var allowedActions = new List<string>();
        if (status == WorkflowStatus.AwaitingHumanApproval)
        {
            allowedActions.Add("SubmitDecision");
        }

        return new IngestionWorkflowProgress(
            response.ExecutionId,
            response.SourceId,
            status,
            stage,
            BuildIngestionStatusMessage(stage, status, response.FailureReason),
            study,
            translation,
            linking,
            null,
            null,
            allowedActions,
            response.FailureReason);
    }

    public static CurationWorkflowProgress ToCurationProgress(CurateWorkflowStatusResponse response)
    {
        var status = ParseWorkflowStatus(response.Status);
        var curation = AgentOutputParser.ParseCurationCompliance(response.CurationOutput);
        var stage = DeriveCurationStage(status, curation, response.CurationOutput);

        var allowedActions = new List<string>();
        if (status == WorkflowStatus.AwaitingHumanApproval)
        {
            allowedActions.Add("SubmitDecision");
        }

        return new CurationWorkflowProgress(
            response.ExecutionId,
            response.SessionId,
            status,
            stage,
            BuildCurationStatusMessage(stage, status, response.FailureReason),
            curation,
            null,
            allowedActions);
    }

    public static QuerySessionState ToQuerySessionState(CachedQuerySession cached)
    {
        var stage = cached.Messages.Count > 0 ? QueryStage.ChatActive : QueryStage.Pending;
        var message = cached.Messages.Count > 0
            ? "Search & Chat active — ask follow-up questions or click Curate when ready."
            : "Ask a research question to begin Search & Chat.";

        return new QuerySessionState(
            cached.SessionId,
            cached.StudyScope,
            cached.Messages.ToList(),
            false,
            cached.CurationExecutionId,
            WorkflowStatus.Pending,
            stage,
            message,
            null,
            null,
            cached.CurateEnabled && cached.Messages.Count > 0 ? ["StartCuration"] : []);
    }

    public static VectorDbStoreSummary ToVectorDbSummary(VectorDbStoreSummaryResponse response) =>
        new(
            response.TotalStudies,
            response.TotalDocuments,
            response.TotalEntities,
            response.TotalLinks,
            response.LastIngestionAt,
            response.LastIngestedStudyId);

    public static ChatMessage ToAssistantMessage(ChatAnswerResponse response)
    {
        var citations = response.Citations
            .Select((citation, index) => new Citation(
                $"cite-{index + 1}",
                citation,
                citation,
                "Vector DB",
                1.0))
            .ToList();

        return new ChatMessage(
            "assistant",
            response.Answer,
            citations,
            null,
            null,
            DateTimeOffset.UtcNow);
    }

    public static WorkflowStatus ParseWorkflowStatus(string status) =>
        Enum.TryParse<WorkflowStatus>(status, ignoreCase: true, out var parsed)
            ? parsed
            : WorkflowStatus.Pending;

    private static IngestionStage DeriveIngestionStage(
        WorkflowStatus status,
        IngestionTranslationResult? translation,
        MetadataLinkingResult? linking,
        string? rawTranslation = null,
        string? rawLinking = null)
    {
        if (status == WorkflowStatus.Failed)
        {
            return IngestionStage.Failed;
        }

        if (status == WorkflowStatus.Completed)
        {
            return IngestionStage.Completed;
        }

        if (status == WorkflowStatus.AwaitingHumanApproval)
        {
            return IngestionStage.HumanApproval;
        }

        if (linking is not null)
        {
            return IngestionStage.HumanApproval;
        }

        if (translation is not null)
        {
            return IngestionStage.MetadataLinking;
        }

        if (status == WorkflowStatus.Running)
        {
            return IngestionStage.IngestionTranslation;
        }

        return IngestionStage.Pending;
    }

    private static QueryStage DeriveCurationStage(
        WorkflowStatus status,
        CurationComplianceResult? curation,
        string? rawCuration = null)
    {
        if (status == WorkflowStatus.Failed)
        {
            return QueryStage.Failed;
        }

        if (status == WorkflowStatus.Completed)
        {
            return QueryStage.Completed;
        }

        if (status == WorkflowStatus.AwaitingHumanApproval)
        {
            return QueryStage.AwaitingComplianceReview;
        }

        if (curation is not null || HasRawOutput(rawCuration) || status == WorkflowStatus.Running)
        {
            return QueryStage.CurationRunning;
        }

        return QueryStage.Pending;
    }

    private static bool HasRawOutput(string? raw) =>
        !string.IsNullOrWhiteSpace(raw);

    private static string BuildIngestionStatusMessage(
        IngestionStage stage,
        WorkflowStatus status,
        string? failureReason)
    {
        if (status == WorkflowStatus.Failed)
        {
            if (IngestionProgressNormalizer.IsKnownPostApprovalWorkflowOutputError(failureReason))
            {
                return IngestionProgressNormalizer.PostApprovalCompletionErrorFallback;
            }

            return failureReason ?? "Ingestion denied or failed.";
        }

        return stage switch
        {
            IngestionStage.IngestionTranslation => "Reading raw R&D knowledge from Microsoft Fabric and normalizing formats…",
            IngestionStage.MetadataLinking => "Extracting entities and linking documents to datasets and studies…",
            IngestionStage.HumanApproval => "Knowledge Curator: review ingested content before writing to Vector DB.",
            IngestionStage.Completed => "Ingestion approved. Knowledge embedded and saved to Vector DB.",
            IngestionStage.Failed => "Ingestion denied or failed.",
            _ => "Preparing ingestion workflow…"
        };
    }

    private static string BuildCurationStatusMessage(
        QueryStage stage,
        WorkflowStatus status,
        string? failureReason)
    {
        if (status == WorkflowStatus.Failed)
        {
            return failureReason ?? "Curation cycle denied or failed.";
        }

        return stage switch
        {
            QueryStage.CurationRunning => "Curation & Compliance reviewing accumulated chat responses…",
            QueryStage.AwaitingComplianceReview => "Compliance Reviewer: review curation flags and captured decisions.",
            QueryStage.Completed => "Curation cycle approved and audited.",
            QueryStage.Failed => "Curation cycle denied or failed.",
            _ => "Preparing curation…"
        };
    }
}
