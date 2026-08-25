using Cohere.AgenticRDKnowledge.Shared.Contracts;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Agents;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Ingestion;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Query;
using Cohere.AgenticRDKnowledge.WebApp.Configuration;
using Cohere.AgenticRDKnowledge.WebApp.Services;
using Microsoft.Extensions.Options;

namespace Cohere.AgenticRDKnowledge.WebApp.State;

public sealed class QueryWorkspaceState : IAsyncDisposable
{
    private readonly IRdKnowledgeApiClient _apiClient;
    private readonly KnowledgeSessionStore _sessionStore;
    private readonly WorkflowPollingOptions _pollingOptions;
    private CancellationTokenSource? _curationPollingCts;
    private HumanDecisionRecord? _lastHumanDecision;

    public string? SessionId { get; private set; }
    public string? CurationExecutionId { get; private set; }
    public string Question { get; private set; } = string.Empty;
    public string? StudyScope { get; private set; }
    public QuerySessionState? Session { get; private set; }
    public CurationWorkflowProgress? CurationProgress { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsCurationPolling { get; private set; }
    public string? Error { get; private set; }
    public string? CurationMessage { get; private set; }

    public bool CanSendMessage =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(SessionId) &&
        CurationProgress?.Status is not (WorkflowStatus.Running or WorkflowStatus.AwaitingHumanApproval or WorkflowStatus.Completed);

    public bool CanStartCuration =>
        !IsBusy &&
        Session?.Messages.Count > 0 &&
        CurationProgress?.Status is not (WorkflowStatus.Running or WorkflowStatus.AwaitingHumanApproval or WorkflowStatus.Completed);

    public bool CanSubmitDecision =>
        CurationProgress?.Status == WorkflowStatus.AwaitingHumanApproval
        && CurationProgress.HumanDecision is null;

    public event Action? OnChange;

    public QueryWorkspaceState(
        IRdKnowledgeApiClient apiClient,
        KnowledgeSessionStore sessionStore,
        IOptions<WorkflowPollingOptions> pollingOptions)
    {
        _apiClient = apiClient;
        _sessionStore = sessionStore;
        _pollingOptions = pollingOptions.Value;
    }

    public async Task LoadAsync(string sessionId, string? curationExecutionId, CancellationToken cancellationToken = default)
    {
        SessionId = sessionId;
        CurationExecutionId = curationExecutionId;
        IsBusy = true;
        Error = null;
        CurationMessage = null;
        Notify();

        try
        {
            var stored = _sessionStore.GetQuerySession(sessionId);
            Question = stored?.SampleQuestion ?? string.Empty;
            StudyScope = stored?.StudyId;

            Session = await _apiClient.GetQuerySessionAsync(sessionId, cancellationToken);
            StudyScope = Session.StudyScope ?? StudyScope;
            UpdateSessionFromQueryState();

            if (Session.Messages.Count == 0 && !string.IsNullOrWhiteSpace(stored?.SampleQuestion))
            {
                Session = await _apiClient.SendChatMessageAsync(
                    sessionId,
                    new SendChatMessageRequest(stored.SampleQuestion, StudyScope),
                    cancellationToken);
                Question = string.Empty;
                UpdateSessionFromQueryState();
            }

            if (!string.IsNullOrWhiteSpace(CurationExecutionId))
            {
                CurationProgress = ApplyCurationNormalization(
                    await _apiClient.GetCurationStatusAsync(CurationExecutionId, cancellationToken));
                UpdateSessionFromCurationProgress();
                if (ShouldPollCuration(CurationProgress.Status))
                {
                    StartCurationPolling();
                }
            }
            else
            {
                StopCurationPolling();
                CurationProgress = null;
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
            Notify();
        }
    }

    public async Task SendMessageAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(SessionId) || string.IsNullOrWhiteSpace(Question) || !CanSendMessage)
        {
            return;
        }

        IsBusy = true;
        Error = null;
        CurationMessage = null;
        Notify();

        try
        {
            Session = await _apiClient.SendChatMessageAsync(
                SessionId,
                new SendChatMessageRequest(Question, StudyScope),
                cancellationToken);
            UpdateSessionFromQueryState();
            Question = string.Empty;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
            Notify();
        }
    }

    public async Task StartCurationAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(SessionId) || !CanStartCuration)
        {
            return;
        }

        IsBusy = true;
        Error = null;
        CurationMessage = null;
        Notify();

        try
        {
            _lastHumanDecision = null;
            var progress = await _apiClient.StartCurationAsync(SessionId, SessionId, cancellationToken);
            if (progress.Status == WorkflowStatus.Failed)
            {
                CurationMessage = progress.StatusMessage;
                return;
            }

            CurationExecutionId = progress.ExecutionId;
            CurationProgress = ApplyCurationNormalization(
                await _apiClient.GetCurationStatusAsync(CurationExecutionId, cancellationToken));
            Session = await _apiClient.GetQuerySessionAsync(SessionId, cancellationToken);
            UpdateSessionFromCurationProgress();
            UpdateSessionFromQueryState();
            StartCurationPolling();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
            Notify();
        }
    }

    public async Task SubmitDecisionAsync(bool approved, string? notes, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(SessionId) || string.IsNullOrWhiteSpace(CurationExecutionId))
        {
            return;
        }

        IsBusy = true;
        Error = null;
        Notify();

        try
        {
            await _apiClient.SubmitCurationDecisionAsync(
                SessionId,
                CurationExecutionId,
                new SubmitQueryDecisionRequest(approved, notes),
                cancellationToken);
            _lastHumanDecision = new HumanDecisionRecord(approved, notes, DateTimeOffset.UtcNow);
            CurationProgress = ApplyCurationNormalization(
                await _apiClient.GetCurationStatusAsync(CurationExecutionId, cancellationToken));
            Session = await _apiClient.GetQuerySessionAsync(SessionId, cancellationToken);
            UpdateSessionFromCurationProgress();
            UpdateSessionFromQueryState();
            StopCurationPolling();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
            Notify();
        }
    }

    public void SetQuestion(string question)
    {
        Question = question;
        Notify();
    }

    private void StartCurationPolling()
    {
        StopCurationPolling();
        _curationPollingCts = new CancellationTokenSource();
        _ = CurationPollLoopAsync(_curationPollingCts.Token);
    }

    private void StopCurationPolling()
    {
        _curationPollingCts?.Cancel();
        _curationPollingCts?.Dispose();
        _curationPollingCts = null;
        IsCurationPolling = false;
    }

    private async Task CurationPollLoopAsync(CancellationToken cancellationToken)
    {
        IsCurationPolling = true;
        var started = DateTimeOffset.UtcNow;
        var maxDuration = TimeSpan.FromMinutes(_pollingOptions.MaxDurationMinutes);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow - started > maxDuration)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(CurationExecutionId) || string.IsNullOrWhiteSpace(SessionId))
                {
                    break;
                }

                CurationProgress = ApplyCurationNormalization(
                    await _apiClient.GetCurationStatusAsync(CurationExecutionId, cancellationToken));
                Session = await _apiClient.GetQuerySessionAsync(SessionId, cancellationToken);
                UpdateSessionFromCurationProgress();
                UpdateSessionFromQueryState();
                Notify();

                if (!ShouldPollCuration(CurationProgress.Status))
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(_pollingOptions.IntervalSeconds), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on dispose.
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Notify();
        }
        finally
        {
            IsCurationPolling = false;
            Notify();
        }
    }

    private static bool ShouldPollCuration(WorkflowStatus status) =>
        status is WorkflowStatus.Running or WorkflowStatus.Pending;

    private CurationWorkflowProgress ApplyCurationNormalization(CurationWorkflowProgress progress) =>
        _lastHumanDecision is not null && progress.HumanDecision is null
            ? progress with { HumanDecision = _lastHumanDecision }
            : progress;

    private void UpdateSessionFromQueryState()
    {
        if (SessionId is null || Session is null)
        {
            return;
        }

        var session = _sessionStore.GetQuerySession(SessionId);
        if (session is null)
        {
            return;
        }

        session.ChatMessageCount = Session.Messages.Count;
        session.ExecutionId = CurationExecutionId;
        session.Status = CurationProgress?.Status ?? (Session.Messages.Count > 0 ? WorkflowStatus.Running : WorkflowStatus.Pending);
        _sessionStore.UpdateSession(session);
    }

    private void UpdateSessionFromCurationProgress()
    {
        if (SessionId is null || CurationProgress is null)
        {
            return;
        }

        var session = _sessionStore.GetQuerySession(SessionId);
        if (session is null)
        {
            return;
        }

        session.ExecutionId = CurationExecutionId;
        session.Status = CurationProgress.Status;
        _sessionStore.UpdateSession(session);
    }

    private void Notify() => OnChange?.Invoke();

    public ValueTask DisposeAsync()
    {
        StopCurationPolling();
        return ValueTask.CompletedTask;
    }
}
