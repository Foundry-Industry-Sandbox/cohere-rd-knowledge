using CohereRndKnowledgeMining.Api.Host.Contracts;
using CohereRndKnowledgeMining.Api.Host.Services.Integrations;
using CohereRndKnowledgeMining.Api.Host.Workflow;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using AgentWorkflow = Microsoft.Agents.AI.Workflows.Workflow;

namespace CohereRndKnowledgeMining.Api.Host.Services;

/// <summary>
/// Drives Block 2 (Query). Process 1 (Search &amp; Chat) is an interactive loop handled with direct
/// agent calls that accumulate responses in the session. Process 2 (Curate) runs the
/// <see cref="QueryWorkflowFactory"/> workflow over those accumulated responses and pauses at the
/// Compliance Reviewer gate.
/// </summary>
public sealed class QueryWorkflowService
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);
    private const int RetrievalTopN = 5;

    private const string CurationComplianceKey = "CurationCompliance";

    private readonly FoundryAgentProvider _agentProvider;
    private readonly QueryWorkflowFactory _workflowFactory;
    private readonly InMemoryQuerySessionStore _store;
    private readonly IVectorKnowledgeRetriever _retriever;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<QueryWorkflowService> _logger;

    public QueryWorkflowService(
        FoundryAgentProvider agentProvider,
        QueryWorkflowFactory workflowFactory,
        InMemoryQuerySessionStore store,
        IVectorKnowledgeRetriever retriever,
        IHostApplicationLifetime applicationLifetime,
        ILogger<QueryWorkflowService> logger)
    {
        _agentProvider = agentProvider;
        _workflowFactory = workflowFactory;
        _store = store;
        _retriever = retriever;
        _applicationLifetime = applicationLifetime;
        _logger = logger;
    }

    // ---- Process 1: Search & Chat (interactive, no gate) ----

    public async Task<ChatAnswerResponse> AskAsync(string sessionId, string question, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("SessionId is required.");
        }

        if (string.IsNullOrWhiteSpace(question))
        {
            throw new InvalidOperationException("Question is required.");
        }

        AIAgent searchChat = await _agentProvider.GetSearchChatAgentAsync(cancellationToken).ConfigureAwait(false);
        QueryChatSession session = _store.GetOrCreateSession(sessionId.Trim());

        IReadOnlyList<RetrievedPassage> passages =
            await _retriever.RetrieveAsync(sessionId, question, RetrievalTopN, cancellationToken)
                .ConfigureAwait(false);

        string prompt = BuildGroundedPrompt(question, passages);
        session.History.Add(new ChatMessage(ChatRole.User, prompt));

        AgentResponse response = await searchChat
            .RunAsync(session.History, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        string rawOutput = WorkflowTextExtractor.GetAgentResponseText(response);
        string answer = ExtractChatAnswer(rawOutput);
        IReadOnlyList<string> citations = MergeCitations(
            passages.Select(passage => passage.Citation),
            rawOutput);

        session.History.Add(new ChatMessage(ChatRole.Assistant, answer));

        bool isGrounded = QuerySessionCurateRules.EvaluateGrounded(answer, citations, passages, rawOutput);

        var turn = new ChatTurn
        {
            Question = question,
            Answer = answer,
            Citations = citations,
            IsGrounded = isGrounded
        };

        session.Turns.Add(turn);
        session.LastUpdatedUtc = DateTimeOffset.UtcNow;

        return new ChatAnswerResponse
        {
            SessionId = session.SessionId,
            Question = question,
            Answer = answer,
            Citations = citations,
            TurnCount = session.Turns.Count,
            CurateEnabled = QuerySessionCurateRules.IsCurateEnabled(session)
        };
    }

    private static string BuildGroundedPrompt(string question, IReadOnlyList<RetrievedPassage> passages)
    {
        if (passages.Count == 0)
        {
            return question;
        }

        string context = string.Join(
            Environment.NewLine,
            passages.Select((passage, index) => $"[{index + 1}] ({passage.Citation}) {passage.Content}"));

        return $"Use the following retrieved context to answer with grounded citations and lineage.{Environment.NewLine}{context}{Environment.NewLine}{Environment.NewLine}Question: {question}";
    }

    private static string ExtractChatAnswer(string rawOutput)
    {
        AgentStructuredOutput? structured = AgentStructuredOutputParser.TryParseStructuredOutput(rawOutput);
        if (structured is not null && !string.IsNullOrWhiteSpace(structured.Summary))
        {
            return structured.Summary.Trim();
        }

        return rawOutput.Trim();
    }

    private static IReadOnlyList<string> MergeCitations(
        IEnumerable<string> retrieverCitations,
        string rawOutput)
    {
        var citations = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? citation)
        {
            if (string.IsNullOrWhiteSpace(citation))
            {
                return;
            }

            string trimmed = citation.Trim();
            if (seen.Add(trimmed))
            {
                citations.Add(trimmed);
            }
        }

        foreach (string citation in retrieverCitations)
        {
            Add(citation);
        }

        AgentStructuredOutput? structured = AgentStructuredOutputParser.TryParseStructuredOutput(rawOutput);
        if (structured?.Citations is not null)
        {
            foreach (string citation in structured.Citations)
            {
                Add(citation);
            }
        }

        return citations;
    }

    // ---- Process 2: Curate (on-demand, Compliance gate) ----

    public CurateWorkflowStatusResponse StartCurate(string sessionId, string executionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("SessionId is required.");
        }

        if (string.IsNullOrWhiteSpace(executionId))
        {
            throw new InvalidOperationException("ExecutionId is required.");
        }

        QueryChatSession session = _store.GetRequiredSession(sessionId.Trim());

        if (!QuerySessionCurateRules.IsCurateEnabled(session))
        {
            return CreateRejectedCurateStart(
                session.SessionId,
                executionId,
                QuerySessionCurateRules.NoGroundedResponsesToCurateMessage);
        }

        IReadOnlyList<string> chatResponses = session.Turns
            .Where(turn => turn.IsGrounded)
            .Select(turn => turn.Answer)
            .ToArray();

        if (chatResponses.Count == 0)
        {
            return CreateRejectedCurateStart(
                session.SessionId,
                executionId,
                QuerySessionCurateRules.NoGroundedResponsesToCurateMessage);
        }

        var execution = new WorkflowExecution
        {
            ExecutionId = executionId,
            CorrelationId = session.SessionId,
            Status = WorkflowStatus.Running,
            WorkflowCheckpointManager = CheckpointManager.CreateInMemory()
        };
        _store.SaveCurateExecution(execution);

        List<ChatMessage> input = WorkflowPayloadBuilder.CreateCurationInputMessages(
            session.SessionId,
            executionId,
            chatResponses);
        RunInBackground(executionId, input);

        return ToResponse(execution);
    }

    public CurateWorkflowStatusResponse GetCurateStatus(string executionId) =>
        ToResponse(_store.GetRequiredCurateExecution(executionId));

    public CurateWorkflowStatusResponse ResumeCurate(
        string sessionId,
        string executionId,
        bool approved,
        string? reviewerComment)
    {
        WorkflowExecution execution = _store.GetRequiredCurateExecution(executionId);

        if (!string.Equals(execution.CorrelationId, sessionId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Curate execution '{executionId}' does not belong to session '{sessionId}'.");
        }

        if (execution.Status != WorkflowStatus.AwaitingHumanApproval ||
            execution.PendingCheckpoint is null ||
            execution.PendingApprovalRequest is null ||
            execution.WorkflowCheckpointManager is null)
        {
            throw new InvalidOperationException("Curate workflow is not waiting for human approval.");
        }

        execution.Status = WorkflowStatus.Running;
        execution.FailureReason = null;
        Touch(execution);

        ResumeInBackground(executionId, approved, reviewerComment);

        return ToResponse(execution);
    }

    private void RunInBackground(string executionId, IList<ChatMessage> input)
    {
        CancellationToken stopping = _applicationLifetime.ApplicationStopping;

        _ = Task.Run(async () =>
        {
            WorkflowExecution execution = _store.GetRequiredCurateExecution(executionId);

            try
            {
                AIAgent curationCompliance = await _agentProvider
                    .GetCurationComplianceAgentAsync(stopping)
                    .ConfigureAwait(false);
                AgentWorkflow workflow = _workflowFactory.CreateWorkflow(
                    curationCompliance,
                    execution.CorrelationId,
                    executionId);

                await using StreamingRun run = await InProcessExecution
                    .RunStreamingAsync(
                        workflow,
                        input,
                        execution.WorkflowCheckpointManager ?? throw new InvalidOperationException("Checkpoint manager was not initialized."),
                        executionId,
                        stopping)
                    .ConfigureAwait(false);

                await RunUntilDoneAsync(execution, run, stopping, sendInitialTurnToken: true).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                MarkFailed(execution, "Workflow cancelled because the application is stopping.");
            }
            catch (OperationCanceledException)
            {
                MarkTimedOut(execution, "Curate workflow");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Curate workflow failed for execution {ExecutionId}.", executionId);
                MarkFailed(execution, ex.Message);
            }
        }, CancellationToken.None);
    }

    private void ResumeInBackground(string executionId, bool approved, string? reviewerComment)
    {
        CancellationToken stopping = _applicationLifetime.ApplicationStopping;

        _ = Task.Run(async () =>
        {
            WorkflowExecution execution = _store.GetRequiredCurateExecution(executionId);

            try
            {
                AIAgent curationCompliance = await _agentProvider
                    .GetCurationComplianceAgentAsync(stopping)
                    .ConfigureAwait(false);
                AgentWorkflow workflow = _workflowFactory.CreateWorkflow(
                    curationCompliance,
                    execution.CorrelationId,
                    executionId);

                await using StreamingRun run = await InProcessExecution
                    .ResumeStreamingAsync(
                        workflow,
                        execution.PendingCheckpoint ?? throw new InvalidOperationException("Pending checkpoint was not initialized."),
                        execution.WorkflowCheckpointManager ?? throw new InvalidOperationException("Checkpoint manager was not initialized."),
                        stopping)
                    .ConfigureAwait(false);

                await ResumeRunAsync(execution, run, approved, reviewerComment, stopping).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                MarkFailed(execution, "Workflow cancelled because the application is stopping.");
            }
            catch (OperationCanceledException)
            {
                MarkTimedOut(execution, "Curate workflow resume");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Curate workflow resume failed for execution {ExecutionId}.", executionId);
                MarkFailed(execution, ex.Message);
            }
        }, CancellationToken.None);
    }

    private async Task RunUntilDoneAsync(
        WorkflowExecution execution,
        StreamingRun run,
        CancellationToken cancellationToken,
        bool sendInitialTurnToken)
    {
        using CancellationTokenSource timeoutCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeoutCts.CancelAfter(RunTimeout);

        if (sendInitialTurnToken)
        {
            await SendTurnTokenAsync(run, timeoutCts.Token).ConfigureAwait(false);
        }

        while (!timeoutCts.IsCancellationRequested)
        {
            using CancellationTokenSource idleCts =
                CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token);

            idleCts.CancelAfter(IdleTimeout);

            try
            {
                await foreach (WorkflowEvent workflowEvent in run
                    .WatchStreamAsync(idleCts.Token)
                    .ConfigureAwait(false))
                {
                    HandleEvent(execution, workflowEvent);

                    if (execution.Status is WorkflowStatus.Completed or WorkflowStatus.Failed or WorkflowStatus.AwaitingHumanApproval)
                    {
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
                when (idleCts.IsCancellationRequested && !timeoutCts.IsCancellationRequested)
            {
                // Idle timeout; query run status and continue driving the workflow.
            }

            if (execution.Status == WorkflowStatus.AwaitingHumanApproval)
            {
                return;
            }

            RunStatus status = await run.GetStatusAsync(timeoutCts.Token).ConfigureAwait(false);

            switch (status)
            {
                case RunStatus.Ended:
                    MarkCompleted(execution);
                    return;

                case RunStatus.PendingRequests:
                    if (execution.Status == WorkflowStatus.AwaitingHumanApproval)
                    {
                        return;
                    }

                    await SendTurnTokenAsync(run, timeoutCts.Token).ConfigureAwait(false);
                    break;

                case RunStatus.Running:
                case RunStatus.Idle:
                    break;

                default:
                    MarkFailed(execution, $"Workflow entered unexpected run status '{status}'.");
                    return;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        MarkTimedOut(execution, "Curate workflow");
    }

    private async Task ResumeRunAsync(
        WorkflowExecution execution,
        StreamingRun run,
        bool approved,
        string? reviewerComment,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeoutCts.CancelAfter(RunTimeout);

        bool responseSent = false;

        while (!timeoutCts.IsCancellationRequested)
        {
            using CancellationTokenSource idleCts =
                CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token);

            idleCts.CancelAfter(IdleTimeout);

            try
            {
                await foreach (WorkflowEvent workflowEvent in run
                    .WatchStreamAsync(idleCts.Token)
                    .ConfigureAwait(false))
                {
                    if (!responseSent &&
                        workflowEvent is RequestInfoEvent requestInfoEvent &&
                        requestInfoEvent.Request.TryGetDataAs(out HumanApprovalPrompt? _))
                    {
                        ExternalResponse response = requestInfoEvent.Request.CreateResponse(
                            new HumanApprovalDecision
                            {
                                Approved = approved,
                                ReviewerComment = reviewerComment
                            });

                        await run.SendResponseAsync(response).ConfigureAwait(false);
                        execution.PendingApprovalRequest = null;
                        execution.PendingCheckpoint = null;
                        execution.Status = WorkflowStatus.Running;
                        execution.FailureReason = null;
                        Touch(execution);
                        responseSent = true;
                        continue;
                    }

                    HandleEvent(execution, workflowEvent);

                    if (execution.Status is WorkflowStatus.Completed or WorkflowStatus.Failed or WorkflowStatus.AwaitingHumanApproval)
                    {
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
                when (idleCts.IsCancellationRequested && !timeoutCts.IsCancellationRequested)
            {
                // Idle timeout; query run status and continue driving the resumed workflow.
            }

            RunStatus status = await run.GetStatusAsync(timeoutCts.Token).ConfigureAwait(false);

            switch (status)
            {
                case RunStatus.Ended:
                    MarkCompleted(execution);
                    return;

                case RunStatus.PendingRequests:
                    if (execution.Status == WorkflowStatus.AwaitingHumanApproval)
                    {
                        return;
                    }

                    break;

                case RunStatus.Running:
                case RunStatus.Idle:
                    if (!responseSent)
                    {
                        continue;
                    }

                    break;

                default:
                    MarkFailed(execution, $"Workflow resume entered unexpected run status '{status}'.");
                    return;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        MarkTimedOut(execution, "Curate workflow resume");
    }

    private void HandleEvent(WorkflowExecution execution, WorkflowEvent workflowEvent)
    {
        _logger.LogInformation("Event {Type}", workflowEvent.GetType().Name);

        switch (workflowEvent)
        {
            case AgentResponseUpdateEvent updateEvent:
                TryUpdateAgentOutput(execution, updateEvent.ExecutorId, updateEvent.AsResponse(), isFinal: false);
                break;

            case AgentResponseEvent responseEvent:
                TryUpdateAgentOutput(execution, responseEvent.ExecutorId, responseEvent.Response, isFinal: true);
                break;

            case ExecutorInvokedEvent invokedEvent:
                MarkExecutorStarted(execution, invokedEvent.ExecutorId);
                break;

            case ExecutorCompletedEvent completedEvent:
                MarkExecutorCompleted(execution, completedEvent.ExecutorId);
                TryUpdateAgentOutput(execution, completedEvent.ExecutorId, completedEvent.Data, isFinal: true);
                break;

            case ExecutorFailedEvent failedEvent:
                string message = failedEvent.Data?.Message
                    ?? $"Executor '{failedEvent.ExecutorId}' failed.";
                MarkFailed(execution, message);
                break;

            case WorkflowOutputEvent:
                MarkCompleted(execution);
                break;

            case RequestInfoEvent requestInfoEvent
                when requestInfoEvent.Request.TryGetDataAs(out HumanApprovalPrompt? _):
                execution.Status = WorkflowStatus.AwaitingHumanApproval;
                execution.CurrentAgent = null;
                execution.PendingApprovalRequest = requestInfoEvent.Request;
                Touch(execution);
                break;

            case SuperStepCompletedEvent superStepCompletedEvent
                when superStepCompletedEvent.CompletionInfo?.Checkpoint is not null:
                execution.PendingCheckpoint = superStepCompletedEvent.CompletionInfo.Checkpoint;
                Touch(execution);
                break;
        }
    }

    private void MarkCompleted(WorkflowExecution execution)
    {
        execution.Status = WorkflowStatus.Completed;
        execution.CurrentAgent = null;
        execution.PendingApprovalRequest = null;
        execution.PendingCheckpoint = null;
        Touch(execution);
    }

    private void MarkExecutorStarted(WorkflowExecution execution, string executorId)
    {
        string? agentKey = MapExecutorToAgentKey(executorId);
        if (agentKey is null)
        {
            return;
        }

        execution.CurrentAgent = agentKey;

        AgentExecutionState state = GetOrCreateAgentState(execution, agentKey);
        state.Status = WorkflowStatus.Running;

        Touch(execution);
    }

    private void MarkExecutorCompleted(WorkflowExecution execution, string executorId)
    {
        string? agentKey = MapExecutorToAgentKey(executorId);
        if (agentKey is null)
        {
            return;
        }

        AgentExecutionState state = GetOrCreateAgentState(execution, agentKey);
        state.Status = WorkflowStatus.Completed;

        if (string.Equals(execution.CurrentAgent, agentKey, StringComparison.OrdinalIgnoreCase))
        {
            execution.CurrentAgent = null;
        }

        Touch(execution);
    }

    private void TryUpdateAgentOutput(
        WorkflowExecution execution,
        string executorId,
        object? data,
        bool isFinal)
    {
        if (data is null)
        {
            return;
        }

        string? rawOutput = data switch
        {
            AgentResponse response => WorkflowTextExtractor.GetAgentResponseText(response),
            ChatMessage[] messages => WorkflowTextExtractor.FromChatMessages(messages),
            IList<ChatMessage> messages => WorkflowTextExtractor.FromChatMessages(messages),
            IEnumerable<ChatMessage> messages => WorkflowTextExtractor.FromChatMessages(messages),
            ChatMessage message => WorkflowTextExtractor.FromChatMessages([message]),
            string text => text,
            _ => null
        };

        if (rawOutput is null)
        {
            return;
        }

        SaveAgentOutput(execution, executorId, rawOutput, isFinal);
    }

    private void SaveAgentOutput(
        WorkflowExecution execution,
        string executorId,
        string rawOutput,
        bool isFinal)
    {
        string? agentKey = MapExecutorToAgentKey(executorId);
        if (agentKey is null)
        {
            return;
        }

        string normalizedOutput = NormalizeAgentOutput(rawOutput);
        if (string.IsNullOrWhiteSpace(normalizedOutput))
        {
            return;
        }

        AgentExecutionState state = GetOrCreateAgentState(execution, agentKey);
        execution.StreamingBuffers.Remove(agentKey);

        if (!isFinal &&
            execution.AgentOutputs.TryGetValue(agentKey, out string? existingOutput) &&
            normalizedOutput.Length <= existingOutput.Length)
        {
            return;
        }

        execution.AgentOutputs[agentKey] = normalizedOutput;
        state.Output = normalizedOutput;
        Touch(execution);
    }

    private static AgentExecutionState GetOrCreateAgentState(WorkflowExecution execution, string agentKey)
    {
        if (execution.Agents.TryGetValue(agentKey, out AgentExecutionState? state))
        {
            return state;
        }

        state = new AgentExecutionState
        {
            AgentName = agentKey,
            Status = WorkflowStatus.Pending
        };

        execution.Agents[agentKey] = state;
        return state;
    }

    private static string NormalizeAgentOutput(string rawOutput)
    {
        string trimmed = rawOutput.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        const string assistantPrefix = "[assistant]";
        int assistantIndex = trimmed.LastIndexOf(assistantPrefix, StringComparison.OrdinalIgnoreCase);
        if (assistantIndex >= 0)
        {
            trimmed = trimmed[(assistantIndex + assistantPrefix.Length)..].TrimStart();
        }

        return trimmed;
    }

    private static string? MapExecutorToAgentKey(string executorId)
    {
        string id = executorId.Replace("_", "-");

        return id.Contains("curation-compliance", StringComparison.OrdinalIgnoreCase)
            ? CurationComplianceKey
            : null;
    }

    private void MarkFailed(WorkflowExecution execution, string reason)
    {
        execution.Status = WorkflowStatus.Failed;
        execution.FailureReason = reason;
        execution.PendingApprovalRequest = null;
        execution.PendingCheckpoint = null;
        Touch(execution);
    }

    private void MarkTimedOut(WorkflowExecution execution, string operation)
    {
        string reason = $"{operation} timed out after {RunTimeout.TotalMinutes:0} minutes.";
        _logger.LogError(
            "{Operation} timed out for execution {ExecutionId}.",
            operation,
            execution.ExecutionId);
        MarkFailed(execution, reason);
    }

    public QuerySessionsResetResponse ResetSessions()
    {
        (int sessions, int curate) = _store.ClearAll();
        return new QuerySessionsResetResponse
        {
            QuerySessionsCleared = sessions,
            CurateExecutionsCleared = curate
        };
    }

    private static void Touch(WorkflowExecution execution)
    {
        execution.LastUpdatedUtc = DateTimeOffset.UtcNow;
    }

    private CurateWorkflowStatusResponse ToResponse(WorkflowExecution execution)
    {
        _store.SaveCurateExecution(execution);

        return new CurateWorkflowStatusResponse
        {
            ExecutionId = execution.ExecutionId,
            SessionId = execution.CorrelationId,
            Status = execution.Status.ToString(),
            CurationOutput = GetOutput(execution, CurationComplianceKey),
            FailureReason = execution.FailureReason,
            LastUpdatedUtc = execution.LastUpdatedUtc
        };
    }

    private static CurateWorkflowStatusResponse CreateRejectedCurateStart(
        string sessionId,
        string executionId,
        string failureReason) =>
        new()
        {
            ExecutionId = executionId,
            SessionId = sessionId,
            Status = WorkflowStatus.Failed.ToString(),
            FailureReason = failureReason,
            LastUpdatedUtc = DateTimeOffset.UtcNow
        };

    private static string? GetOutput(WorkflowExecution execution, string key) =>
        execution.AgentOutputs.TryGetValue(key, out string? value) ? value : null;

    private static async Task SendTurnTokenAsync(StreamingRun run, CancellationToken cancellationToken)
    {
        if (!await run.TrySendMessageAsync(new TurnToken(emitEvents: true)).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Failed to send TurnToken to the workflow.");
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
