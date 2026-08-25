using System.Text.Json;
using System.Text.Json.Serialization;
using Cohere.AgenticRDKnowledge.Shared.Contracts;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Agents;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Ingestion;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Query;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Studies;

namespace Cohere.AgenticRDKnowledge.WebApp.Services;

public static class BackendWorkflowMapper
{
    public static IReadOnlyList<WorkflowTimelineStep> BuildIngestionTimeline(IngestionWorkflowProgress progress)
    {
        return
        [
            new WorkflowTimelineStep(
                "Ingestion & Translation",
                GetIngestionStepState(progress.CurrentStage, IngestionStage.IngestionTranslation)),
            new WorkflowTimelineStep(
                "Metadata & Linking",
                GetIngestionStepState(progress.CurrentStage, IngestionStage.MetadataLinking)),
            new WorkflowTimelineStep(
                "Knowledge Curator",
                GetIngestionStepState(progress.CurrentStage, IngestionStage.HumanApproval))
        ];
    }

    public static IReadOnlyList<WorkflowTimelineStep> BuildQueryTimeline(
        QuerySessionState? session,
        CurationWorkflowProgress? curation)
    {
        var chatState = GetChatStepState(session);
        var curateState = GetCurateStepState(session, curation);

        return
        [
            new WorkflowTimelineStep("Process 1 — Search & Chat", chatState),
            new WorkflowTimelineStep("Process 2 — Curate", curateState)
        ];
    }

    public static IngestionWorkflowProgress MapBackendProgress(BackendIngestionProgressResponse backend)
    {
        return new IngestionWorkflowProgress(
            backend.ExecutionId,
            backend.CaseId,
            Enum.TryParse<WorkflowStatus>(backend.Status, true, out var status)
                ? status
                : WorkflowStatus.Pending,
            Enum.TryParse<IngestionStage>(backend.CurrentStage, true, out var stage)
                ? stage
                : IngestionStage.Pending,
            backend.StatusMessage ?? string.Empty,
            backend.Study is null
                ? null
                : new StudySummary(
                    backend.Study.StudyId,
                    backend.Study.Title,
                    backend.Study.Compound,
                    backend.Study.Phase,
                    backend.Study.PrimaryEndpoint,
                    backend.Study.SourceSystems),
            backend.IngestionTranslation is null
                ? null
                : new IngestionTranslationResult(
                    backend.IngestionTranslation.Summary ?? string.Empty,
                    backend.IngestionTranslation.DocumentsProcessed ?? 0,
                    backend.IngestionTranslation.DuplicatesRemoved ?? 0,
                    backend.IngestionTranslation.NormalizedFormats ?? [],
                    backend.IngestionTranslation.ConnectedPortals ?? []),
            backend.MetadataLinking is null
                ? null
                : new MetadataLinkingResult(
                    backend.MetadataLinking.Summary ?? string.Empty,
                    backend.MetadataLinking.Entities?.Select(e => new EntityChip(e.Name, e.Category, e.Version)).ToList() ?? [],
                    backend.MetadataLinking.Links?.Select(l => new DocumentLink(l.FromDocument, l.ToTarget, l.Relationship)).ToList() ?? [],
                    backend.MetadataLinking.VectorsIndexed),
            backend.RetrievalTrace?.Select(r => new RetrievalTraceEvent(r.Stage, r.Description, r.ItemCount, r.Timestamp)).ToList() ?? [],
            backend.HumanDecision is null
                ? null
                : new HumanDecisionRecord(backend.HumanDecision.Approved, backend.HumanDecision.Notes, backend.HumanDecision.DecidedAt),
            backend.AllowedActions ?? [],
            backend.FailureReason);
    }

    private static WorkflowStepState GetChatStepState(QuerySessionState? session)
    {
        if (session is null || session.Messages.Count == 0)
        {
            return WorkflowStepState.Pending;
        }

        if (session.IsChatRunning)
        {
            return WorkflowStepState.InProgress;
        }

        return WorkflowStepState.Completed;
    }

    private static WorkflowStepState GetCurateStepState(QuerySessionState? session, CurationWorkflowProgress? curation)
    {
        if (curation is null)
        {
            return session?.Messages.Count > 0 ? WorkflowStepState.Pending : WorkflowStepState.Pending;
        }

        return curation.CurrentStage switch
        {
            QueryStage.CurationRunning => WorkflowStepState.InProgress,
            QueryStage.AwaitingComplianceReview => WorkflowStepState.ActionRequired,
            QueryStage.Completed => WorkflowStepState.Completed,
            QueryStage.Failed => WorkflowStepState.Failed,
            _ => WorkflowStepState.Pending
        };
    }

    private static WorkflowStepState GetIngestionStepState(IngestionStage current, IngestionStage step)
    {
        if (current == IngestionStage.Failed)
        {
            return step <= current ? WorkflowStepState.Failed : WorkflowStepState.Pending;
        }

        if (current == IngestionStage.Completed)
        {
            return WorkflowStepState.Completed;
        }

        if (current > step)
        {
            return WorkflowStepState.Completed;
        }

        if (current == step)
        {
            return current == IngestionStage.HumanApproval && step == IngestionStage.HumanApproval
                ? WorkflowStepState.ActionRequired
                : WorkflowStepState.InProgress;
        }

        return WorkflowStepState.Pending;
    }
}

public sealed class BackendIngestionProgressResponse
{
    [JsonPropertyName("executionId")]
    public string ExecutionId { get; set; } = string.Empty;

    [JsonPropertyName("caseId")]
    public string CaseId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("currentStage")]
    public string? CurrentStage { get; set; }

    [JsonPropertyName("statusMessage")]
    public string? StatusMessage { get; set; }

    [JsonPropertyName("study")]
    public BackendStudySummaryResponse? Study { get; set; }

    [JsonPropertyName("ingestionTranslation")]
    public BackendIngestionTranslationResponse? IngestionTranslation { get; set; }

    [JsonPropertyName("metadataLinking")]
    public BackendMetadataLinkingResponse? MetadataLinking { get; set; }

    [JsonPropertyName("retrievalTrace")]
    public List<BackendRetrievalTraceEventResponse>? RetrievalTrace { get; set; }

    [JsonPropertyName("humanDecision")]
    public BackendHumanDecisionResponse? HumanDecision { get; set; }

    [JsonPropertyName("allowedActions")]
    public List<string>? AllowedActions { get; set; }

    [JsonPropertyName("failureReason")]
    public string? FailureReason { get; set; }
}

public sealed class BackendStartIngestionResponse
{
    [JsonPropertyName("executionId")]
    public string ExecutionId { get; set; } = string.Empty;

    [JsonPropertyName("caseId")]
    public string CaseId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

public sealed class BackendStudySummaryResponse
{
    [JsonPropertyName("studyId")]
    public string StudyId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("compound")]
    public string Compound { get; set; } = string.Empty;

    [JsonPropertyName("phase")]
    public string Phase { get; set; } = string.Empty;

    [JsonPropertyName("primaryEndpoint")]
    public string PrimaryEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("sourceSystems")]
    public List<string> SourceSystems { get; set; } = [];
}

public sealed class BackendIngestionTranslationResponse
{
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("documentsProcessed")]
    public int? DocumentsProcessed { get; set; }

    [JsonPropertyName("duplicatesRemoved")]
    public int? DuplicatesRemoved { get; set; }

    [JsonPropertyName("normalizedFormats")]
    public List<string>? NormalizedFormats { get; set; }

    [JsonPropertyName("connectedPortals")]
    public List<string>? ConnectedPortals { get; set; }
}

public sealed class BackendMetadataLinkingResponse
{
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("entities")]
    public List<BackendEntityChipResponse>? Entities { get; set; }

    [JsonPropertyName("links")]
    public List<BackendDocumentLinkResponse>? Links { get; set; }

    [JsonPropertyName("vectorsIndexed")]
    public int VectorsIndexed { get; set; }
}

public sealed class BackendEntityChipResponse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}

public sealed class BackendDocumentLinkResponse
{
    [JsonPropertyName("fromDocument")]
    public string FromDocument { get; set; } = string.Empty;

    [JsonPropertyName("toTarget")]
    public string ToTarget { get; set; } = string.Empty;

    [JsonPropertyName("relationship")]
    public string Relationship { get; set; } = string.Empty;
}

public sealed class BackendRetrievalTraceEventResponse
{
    [JsonPropertyName("stage")]
    public string Stage { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("itemCount")]
    public int ItemCount { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }
}

public sealed class BackendHumanDecisionResponse
{
    [JsonPropertyName("approved")]
    public bool Approved { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("decidedAt")]
    public DateTimeOffset DecidedAt { get; set; }
}

public enum WorkflowStepState
{
    Pending,
    InProgress,
    ActionRequired,
    Completed,
    Failed
}

public sealed record WorkflowTimelineStep(string Label, WorkflowStepState State);

public static class AgentOutputParser
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static IngestionTranslationResult? ParseIngestionTranslation(string? raw) =>
        ParseWithSchemaFallback(
            raw,
            json => JsonSerializer.Deserialize<IngestionTranslationResult>(json, JsonOptions),
            result => !string.IsNullOrWhiteSpace(result?.Summary)
                && result.NormalizedFormats is not null
                && result.ConnectedPortals is not null,
            AgentOutputSchemaMapper.MapIngestionTranslation);

    public static MetadataLinkingResult? ParseMetadataLinking(string? raw) =>
        ParseWithSchemaFallback(
            raw,
            json => JsonSerializer.Deserialize<MetadataLinkingResult>(json, JsonOptions),
            result => !string.IsNullOrWhiteSpace(result?.Summary)
                && result.Entities is not null
                && result.Links is not null,
            AgentOutputSchemaMapper.MapMetadataLinking);

    public static SearchChatResult? ParseSearchChat(string? raw) =>
        ParseWithSchemaFallback(
            raw,
            json => JsonSerializer.Deserialize<SearchChatResult>(json, JsonOptions),
            result => !string.IsNullOrWhiteSpace(result?.Answer),
            AgentOutputSchemaMapper.MapSearchChat);

    public static CurationComplianceResult? ParseCurationCompliance(string? raw)
    {
        var parsed = ParseWithSchemaFallback(
            raw,
            json => JsonSerializer.Deserialize<CurationComplianceResult>(json, JsonOptions),
            result => !string.IsNullOrWhiteSpace(result?.Summary)
                && result.Flags is not null
                && result.PromptedOwners is not null,
            AgentOutputSchemaMapper.MapCurationCompliance);

        return NormalizeCurationCompliance(parsed);
    }

    private static CurationComplianceResult? NormalizeCurationCompliance(CurationComplianceResult? result) =>
        result is null
            ? null
            : result with
            {
                Flags = result.Flags ?? [],
                PromptedOwners = result.PromptedOwners ?? []
            };

    private static T? ParseWithSchemaFallback<T>(
        string? raw,
        Func<string, T?> tryLegacy,
        Func<T?, bool> isUsable,
        Func<string, T?> trySchema) where T : class
    {
        var normalized = NormalizePayload(raw);
        if (normalized is null)
        {
            return null;
        }

        try
        {
            T? legacy = null;
            try
            {
                legacy = tryLegacy(normalized);
            }
            catch (JsonException)
            {
                // Legacy DTO shape mismatch; fall through to schema mapper.
            }

            if (isUsable(legacy))
            {
                return legacy;
            }

            return trySchema(normalized);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string? NormalizePayload(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();

        const string assistantPrefix = "[assistant]";
        var assistantIndex = trimmed.LastIndexOf(assistantPrefix, StringComparison.OrdinalIgnoreCase);
        if (assistantIndex >= 0)
        {
            trimmed = trimmed[(assistantIndex + assistantPrefix.Length)..].TrimStart();
        }

        trimmed = StripWorkflowJsonInputPrefix(trimmed);
        trimmed = ExtractJsonFromMarkdownFence(trimmed);
        trimmed = ExtractLeadingJsonObject(trimmed);

        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '['))
        {
            return null;
        }

        return trimmed;
    }

    private static string StripWorkflowJsonInputPrefix(string value)
    {
        const string marker = "Process the following JSON workflow input";
        var markerIndex = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return value;
        }

        var jsonStart = value.IndexOf('{', markerIndex);
        return jsonStart >= 0 ? value[jsonStart..].Trim() : value;
    }

    private static string ExtractLeadingJsonObject(string value)
    {
        var start = value.IndexOf('{');
        if (start < 0)
        {
            return value;
        }

        if (start == 0 || char.IsWhiteSpace(value[start - 1]))
        {
            return value[start..].Trim();
        }

        return value;
    }

    private static string ExtractJsonFromMarkdownFence(string value)
    {
        const string jsonFence = "```json";
        var start = value.IndexOf(jsonFence, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            const string genericFence = "```";
            start = value.IndexOf(genericFence, StringComparison.Ordinal);
            if (start < 0)
            {
                return value;
            }

            start += genericFence.Length;
        }
        else
        {
            start += jsonFence.Length;
        }

        var end = value.IndexOf("```", start, StringComparison.Ordinal);
        return end < 0 ? value[start..].Trim() : value[start..end].Trim();
    }
}
