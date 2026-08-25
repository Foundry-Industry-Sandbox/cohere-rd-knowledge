using System.Net.Http.Json;
using System.Text.Json;
using Cohere.AgenticRDKnowledge.Shared.Contracts;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Backend;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Ingestion;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Query;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Studies;
using Cohere.AgenticRDKnowledge.Shared.Contracts.VectorDb;
using Cohere.AgenticRDKnowledge.WebApp.Models;

namespace Cohere.AgenticRDKnowledge.WebApp.Services;

public interface IRdKnowledgeApiClient
{
    Task<IReadOnlyList<SeedScenarioDefinition>> GetPortfolioScenariosAsync(CancellationToken cancellationToken = default);
    Task<VectorDbStoreSummary> GetVectorDbStoreSummaryAsync(CancellationToken cancellationToken = default);
    Task<StudyDocumentsResponse> GetStudyDocumentsAsync(string studyId, CancellationToken cancellationToken = default);
    Task<StartIngestionWorkflowResponse> StartIngestionWorkflowAsync(string sourceId, CancellationToken cancellationToken = default);
    Task<IngestionWorkflowProgress> GetIngestionStatusAsync(string executionId, CancellationToken cancellationToken = default);
    Task<SubmitIngestionDecisionResponse> SubmitIngestionDecisionAsync(string executionId, string sourceId, SubmitIngestionDecisionRequest request, CancellationToken cancellationToken = default);
    Task<QuerySessionState> GetQuerySessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<QuerySessionState> SendChatMessageAsync(string sessionId, SendChatMessageRequest request, CancellationToken cancellationToken = default);
    Task<CurationWorkflowProgress> StartCurationAsync(string sessionId, string? executionId = null, CancellationToken cancellationToken = default);
    Task<CurationWorkflowProgress> GetCurationStatusAsync(string curationExecutionId, CancellationToken cancellationToken = default);
    Task<SubmitQueryDecisionResponse> SubmitCurationDecisionAsync(string sessionId, string curationExecutionId, SubmitQueryDecisionRequest request, CancellationToken cancellationToken = default);
    Task<VectorDbClearDataResult> ClearVectorDbAsync(CancellationToken cancellationToken = default);
    Task<QuerySessionsResetResult> ClearQuerySessionsAsync(CancellationToken cancellationToken = default);
    Task ResetDemoAsync(CancellationToken cancellationToken = default);
    Task<bool> GetHealthAsync(CancellationToken cancellationToken = default);
}

public sealed class RdKnowledgeApiClient(
    HttpClient httpClient,
    QuerySessionCache querySessionCache) : IRdKnowledgeApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<SeedScenarioDefinition>> GetPortfolioScenariosAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<List<SeedScenarioDefinition>>(RdKnowledgeBackendRoutes.GetPortfolioScenarios, cancellationToken)
            .ConfigureAwait(false);

    public Task<VectorDbStoreSummary> GetVectorDbStoreSummaryAsync(CancellationToken cancellationToken = default) =>
        GetAsync<VectorDbStoreSummary>(RdKnowledgeBackendRoutes.GetVectorDbStoreSummary, cancellationToken);

    public Task<StudyDocumentsResponse> GetStudyDocumentsAsync(string studyId, CancellationToken cancellationToken = default) =>
        GetAsync<StudyDocumentsResponse>(
            RdKnowledgeBackendRoutes.GetStudyDocuments.Replace("{caseId}", Uri.EscapeDataString(studyId)),
            cancellationToken);

    public async Task<StartIngestionWorkflowResponse> StartIngestionWorkflowAsync(
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        var response = await PostAsync<IngestionWorkflowStatusResponse>(
            RdKnowledgeBackendRoutes.StartIngestion,
            new StartIngestionRequest { SourceId = sourceId },
            cancellationToken);

        return new StartIngestionWorkflowResponse(
            response.ExecutionId,
            response.SourceId,
            BackendApiMapper.ParseWorkflowStatus(response.Status));
    }

    public async Task<IngestionWorkflowProgress> GetIngestionStatusAsync(
        string executionId,
        CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<IngestionWorkflowStatusResponse>(
            RdKnowledgeBackendRoutes.GetIngestionStatus.Replace("{executionId}", Uri.EscapeDataString(executionId)),
            cancellationToken);

        return BackendApiMapper.ToIngestionProgress(response);
    }

    public async Task<SubmitIngestionDecisionResponse> SubmitIngestionDecisionAsync(
        string executionId,
        string sourceId,
        SubmitIngestionDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        var path = RdKnowledgeBackendRoutes.SubmitIngestionDecision
            .Replace("{sourceId}", Uri.EscapeDataString(sourceId))
            .Replace("{executionId}", Uri.EscapeDataString(executionId));

        var response = await PostAsync<IngestionWorkflowStatusResponse>(
            path,
            new WorkflowApprovalRequest
            {
                Approved = request.Approved,
                ReviewerComment = request.Notes
            },
            cancellationToken);

        return new SubmitIngestionDecisionResponse(
            response.ExecutionId,
            BackendApiMapper.ParseWorkflowStatus(response.Status));
    }

    public Task<QuerySessionState> GetQuerySessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var cached = querySessionCache.TryGet(sessionId)
            ?? querySessionCache.GetOrCreate(sessionId);
        return Task.FromResult(BackendApiMapper.ToQuerySessionState(cached));
    }

    public async Task<QuerySessionState> SendChatMessageAsync(
        string sessionId,
        SendChatMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        var cached = querySessionCache.GetOrCreate(sessionId, request.StudyScope);
        if (!string.IsNullOrWhiteSpace(request.StudyScope))
        {
            cached.StudyScope = request.StudyScope;
        }

        cached.Messages.Add(new ChatMessage(
            "user",
            request.Question,
            null,
            null,
            null,
            DateTimeOffset.UtcNow));

        var response = await PostAsync<ChatAnswerResponse>(
            RdKnowledgeBackendRoutes.Ask,
            new AskRequest { SessionId = sessionId, Question = request.Question },
            cancellationToken);

        cached.Messages.Add(BackendApiMapper.ToAssistantMessage(response));
        cached.CurateEnabled = response.CurateEnabled;

        return BackendApiMapper.ToQuerySessionState(cached);
    }

    public async Task<CurationWorkflowProgress> StartCurationAsync(
        string sessionId,
        string? executionId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await PostAsync<CurateWorkflowStatusResponse>(
            RdKnowledgeBackendRoutes.StartCurate,
            new StartCurateRequest { SessionId = sessionId, ExecutionId = executionId },
            cancellationToken);

        if (!string.Equals(response.Status, nameof(WorkflowStatus.Failed), StringComparison.OrdinalIgnoreCase))
        {
            querySessionCache.SetCurationExecutionId(sessionId, response.ExecutionId);
        }

        return BackendApiMapper.ToCurationProgress(response);
    }

    public async Task<CurationWorkflowProgress> GetCurationStatusAsync(
        string curationExecutionId,
        CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<CurateWorkflowStatusResponse>(
            RdKnowledgeBackendRoutes.GetCurateStatus.Replace("{executionId}", Uri.EscapeDataString(curationExecutionId)),
            cancellationToken);

        return BackendApiMapper.ToCurationProgress(response);
    }

    public async Task<SubmitQueryDecisionResponse> SubmitCurationDecisionAsync(
        string sessionId,
        string curationExecutionId,
        SubmitQueryDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        var path = RdKnowledgeBackendRoutes.SubmitCurateDecision
            .Replace("{sessionId}", Uri.EscapeDataString(sessionId))
            .Replace("{executionId}", Uri.EscapeDataString(curationExecutionId));

        var response = await PostAsync<CurateWorkflowStatusResponse>(
            path,
            new WorkflowApprovalRequest
            {
                Approved = request.Approved,
                ReviewerComment = request.Notes
            },
            cancellationToken);

        return new SubmitQueryDecisionResponse(
            response.ExecutionId,
            BackendApiMapper.ParseWorkflowStatus(response.Status));
    }

    public Task<VectorDbClearDataResult> ClearVectorDbAsync(CancellationToken cancellationToken = default) =>
        PostAsync<VectorDbClearDataResult>(RdKnowledgeBackendRoutes.ClearVectorDbData, new { }, cancellationToken);

    public Task<QuerySessionsResetResult> ClearQuerySessionsAsync(CancellationToken cancellationToken = default) =>
        PostAsync<QuerySessionsResetResult>(RdKnowledgeBackendRoutes.ResetQuerySessions, new { }, cancellationToken);

    public async Task ResetDemoAsync(CancellationToken cancellationToken = default)
    {
        await ClearVectorDbAsync(cancellationToken).ConfigureAwait(false);
        await ClearQuerySessionsAsync(cancellationToken).ConfigureAwait(false);
        querySessionCache.ClearAll();
    }

    public async Task<bool> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(RdKnowledgeBackendRoutes.Health, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync(path, cancellationToken);
        await ApiProblemDetails.EnsureSuccessOrThrowAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new InvalidOperationException($"Empty response from {path}.");
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync(path, body, JsonOptions, cancellationToken);
        await ApiProblemDetails.EnsureSuccessOrThrowAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new InvalidOperationException($"Empty response from {path}.");
    }
}

public static class ApiProblemDetails
{
    public static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        string message;
        try
        {
            using var doc = JsonDocument.Parse(body);
            message = doc.RootElement.TryGetProperty("detail", out var detail)
                ? detail.GetString() ?? response.ReasonPhrase ?? "Request failed."
                : response.ReasonPhrase ?? "Request failed.";
        }
        catch
        {
            message = response.ReasonPhrase ?? "Request failed.";
        }

        throw new HttpRequestException(message, null, response.StatusCode);
    }
}
