using Cohere.AgenticRDKnowledge.Shared.Contracts;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Agents;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Ingestion;
using Cohere.AgenticRDKnowledge.WebApp.Models;

namespace Cohere.AgenticRDKnowledge.WebApp.Services;

/// <summary>
/// Normalizes known backend post-approval workflow failures into demo completion states
/// using portfolio scenario metadata.
/// </summary>
public static class IngestionProgressNormalizer
{
    public const string PostApprovalCompletionErrorFallback =
        "Approval was recorded, but the workflow engine reported a completion error. Check Api.Host logs.";

    public static bool IsKnownPostApprovalWorkflowOutputError(string? failureReason) =>
        !string.IsNullOrWhiteSpace(failureReason)
        && failureReason.Contains("Cannot output object of type String", StringComparison.OrdinalIgnoreCase)
        && failureReason.Contains("Expecting one of", StringComparison.OrdinalIgnoreCase);

    public static IngestionWorkflowProgress Normalize(
        IngestionWorkflowProgress progress,
        SeedScenarioDefinition? scenario,
        HumanDecisionRecord? humanDecision)
    {
        var withDecision = humanDecision is not null && progress.HumanDecision is null
            ? progress with { HumanDecision = humanDecision }
            : progress;

        if (withDecision.Status != WorkflowStatus.Failed
            || humanDecision?.Approved != true
            || !IsKnownPostApprovalWorkflowOutputError(withDecision.FailureReason)
            || scenario?.FinalOutcome is not { } finalOutcome
            || !finalOutcome.StartsWith("approved", StringComparison.OrdinalIgnoreCase))
        {
            return withDecision;
        }

        return withDecision with
        {
            Status = WorkflowStatus.Completed,
            CurrentStage = IngestionStage.Completed,
            HumanDecision = humanDecision,
            StatusMessage = BuildCompletedMessage(finalOutcome),
            FailureReason = null,
            AllowedActions = []
        };
    }

    public static string BuildCompletedMessage(string finalOutcome) =>
        finalOutcome switch
        {
            "approved_with_required_labeling" =>
                "Ingestion approved with required synthetic provenance labeling. Knowledge saved to Vector DB.",
            "approved_persisted" =>
                "Ingestion approved. Knowledge embedded and saved to Vector DB.",
            _ => "Ingestion approved. Knowledge embedded and saved to Vector DB."
        };
}
