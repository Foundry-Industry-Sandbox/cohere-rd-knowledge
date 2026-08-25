using CohereRndKnowledgeMining.Api.Host.Contracts;
using CohereRndKnowledgeMining.Api.Host.Workflow;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using AgentWorkflow = Microsoft.Agents.AI.Workflows.Workflow;

namespace CohereRndKnowledgeMining.Api.Host.Services;

/// <summary>
/// Drives the Block 1 (Ingestion) workflow: reads raw knowledge (local or Fabric), runs the
/// ingestion-translation -> metadata-linking -> Knowledge Curator gate graph.
/// Vector DB indexing is performed by the metadata-linking agent via MCP, not gated by curator approval.
/// </summary>
public sealed class IngestionWorkflowService
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(3);

    private const string IngestionTranslationKey = IngestionWorkflowConstants.IngestionTranslationOutputKey;
    private const string MetadataLinkingKey = IngestionWorkflowConstants.MetadataLinkingOutputKey;

    private readonly FoundryAgentProvider _agentProvider;
    private readonly IngestionWorkflowFactory _workflowFactory;
    private readonly InMemoryIngestionWorkflowStore _store;
    private readonly IngestionSourceDocumentLoader _documentLoader;
    private readonly IngestionSourceDocumentCache _sourceDocumentCache;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<IngestionWorkflowService> _logger;

    public IngestionWorkflowService(
        FoundryAgentProvider agentProvider,
        IngestionWorkflowFactory workflowFactory,
        InMemoryIngestionWorkflowStore store,
        IngestionSourceDocumentLoader documentLoader,
        IngestionSourceDocumentCache sourceDocumentCache,
        IHostApplicationLifetime applicationLifetime,
        ILogger<IngestionWorkflowService> logger)
    {
        _agentProvider = agentProvider;
        _workflowFactory = workflowFactory;
        _store = store;
        _documentLoader = documentLoader;
        _sourceDocumentCache = sourceDocumentCache;
        _applicationLifetime = applicationLifetime;
        _logger = logger;
    }

    public async Task<IngestionWorkflowStatusResponse> StartIngestionAsync(
        string sourceId,
        string executionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            throw new InvalidOperationException("SourceId is required.");
        }

        if (string.IsNullOrWhiteSpace(executionId))
        {
            throw new InvalidOperationException("ExecutionId is required.");
        }

        IReadOnlyList<RawKnowledgeItem> items =
            await _documentLoader.LoadAsync(sourceId.Trim(), cancellationToken).ConfigureAwait(false);

        if (items.Count == 0)
        {
            throw new KeyNotFoundException(
                $"Source '{sourceId}' has no raw R&D knowledge items.");
        }

        var execution = new WorkflowExecution
        {
            ExecutionId = executionId,
            CorrelationId = sourceId.Trim(),
            Status = WorkflowStatus.Running,
            WorkflowCheckpointManager = CheckpointManager.CreateInMemory()
        };
        _store.Save(execution);
        _sourceDocumentCache.Save(executionId, items);

        List<ChatMessage> input = WorkflowPayloadBuilder.CreateInlineIngestionMessage(
            sourceId.Trim(),
            executionId,
            items);

        WorkflowTokenDiagnostics.LogInlineIngestionPayload(
            _logger,
            sourceId.Trim(),
            executionId,
            items,
            input);

        RunInBackground(executionId, input);

        return ToResponse(execution);
    }

    public IngestionWorkflowStatusResponse GetIngestionStatus(string executionId) =>
        ToResponse(_store.GetRequired(executionId));

    public IngestionWorkflowStatusResponse ResumeIngestionAsync(
        string sourceId,
        string executionId,
        bool approved,
        string? reviewerComment,
        CancellationToken cancellationToken)
    {
        WorkflowExecution execution = _store.GetRequired(executionId);

        if (!string.Equals(execution.CorrelationId, sourceId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Execution '{executionId}' does not belong to source '{sourceId}'.");
        }

        if (execution.Status != WorkflowStatus.AwaitingHumanApproval ||
            execution.PendingCheckpoint is null ||
            execution.PendingApprovalRequest is null ||
            execution.WorkflowCheckpointManager is null)
        {
            throw new InvalidOperationException(
                "Ingestion workflow is not waiting for human approval.");
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
            WorkflowExecution execution = _store.GetRequired(executionId);

            try
            {
                AIAgent ingestionTranslation = await _agentProvider
                    .GetIngestionTranslationAgentAsync(stopping)
                    .ConfigureAwait(false);
                AIAgent metadataLinking = await _agentProvider
                    .GetMetadataLinkingAgentAsync(stopping)
                    .ConfigureAwait(false);
                AgentWorkflow workflow = _workflowFactory.CreateWorkflow(
                    ingestionTranslation,
                    metadataLinking,
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
                MarkTimedOut(execution, "Ingestion workflow");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ingestion workflow failed for execution {ExecutionId}.", executionId);
                MarkFailed(execution, ex.Message);
            }
        }, CancellationToken.None);
    }

    private void ResumeInBackground(string executionId, bool approved, string? reviewerComment)
    {
        CancellationToken stopping = _applicationLifetime.ApplicationStopping;

        _ = Task.Run(async () =>
        {
            WorkflowExecution execution = _store.GetRequired(executionId);

            try
            {
                AIAgent ingestionTranslation = await _agentProvider
                    .GetIngestionTranslationAgentAsync(stopping)
                    .ConfigureAwait(false);
                AIAgent metadataLinking = await _agentProvider
                    .GetMetadataLinkingAgentAsync(stopping)
                    .ConfigureAwait(false);
                AgentWorkflow workflow = _workflowFactory.CreateWorkflow(
                    ingestionTranslation,
                    metadataLinking,
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
                MarkTimedOut(execution, "Ingestion workflow resume");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ingestion workflow resume failed for execution {ExecutionId}.", executionId);
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
        MarkTimedOut(execution, "Ingestion workflow");
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
        MarkTimedOut(execution, "Ingestion workflow resume");
    }

    private void HandleEvent(WorkflowExecution execution, WorkflowEvent workflowEvent)
    {
        switch (workflowEvent)
        {
            case AgentResponseUpdateEvent updateEvent:
                TryUpdateAgentOutput(execution, updateEvent.ExecutorId, updateEvent.AsResponse(), isFinal: false);
                break;

            case AgentResponseEvent responseEvent:
                WorkflowTokenDiagnostics.LogAgentTurn(
                    _logger,
                    responseEvent.ExecutorId,
                    responseEvent.Response,
                    isFinal: true);
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
            {
                string message = failedEvent.Data?.Message
                    ?? $"Executor '{failedEvent.ExecutorId}' failed.";
                int? lastOutputChars = TryGetLastOutputCharCount(execution, failedEvent.ExecutorId);
                WorkflowTokenDiagnostics.LogExecutorFailure(
                    _logger,
                    failedEvent.ExecutorId,
                    message,
                    lastOutputChars);
                MarkFailed(execution, message);
                break;
            }

            case WorkflowOutputEvent:
                MarkCompleted(execution);
                break;

            case RequestInfoEvent requestInfoEvent
                when requestInfoEvent.Request.TryGetDataAs(out HumanApprovalPrompt? prompt):
                execution.Status = WorkflowStatus.AwaitingHumanApproval;
                execution.CurrentAgent = null;
                execution.PendingApprovalRequest = requestInfoEvent.Request;
                if (!string.IsNullOrWhiteSpace(prompt.ReviewedOutput))
                {
                    AgentStepResult? parsedOutput = AgentStructuredOutputParser.TryParse(
                        IngestionWorkflowConstants.MetadataLinkingAgentName,
                        prompt.ReviewedOutput);
                    string outputToSave = !string.IsNullOrWhiteSpace(parsedOutput?.RawPayloadJson)
                        ? parsedOutput.RawPayloadJson
                        : prompt.ReviewedOutput;

                    SaveAgentOutput(
                        execution,
                        IngestionWorkflowConstants.MetadataLinkingAgentName,
                        outputToSave,
                        isFinal: true);
                }
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
        _sourceDocumentCache.Remove(execution.ExecutionId);
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

    private static int? TryGetLastOutputCharCount(WorkflowExecution execution, string executorId)
    {
        string? agentKey = MapExecutorToAgentKey(executorId);
        if (agentKey is null)
        {
            return null;
        }

        if (!execution.AgentOutputs.TryGetValue(agentKey, out string? output)
            || string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        return output.Length;
    }

    private static string? MapExecutorToAgentKey(string executorId)
    {
        string id = executorId.Replace("_", "-");

        if (id.Contains("ingestion-translation", StringComparison.OrdinalIgnoreCase))
        {
            return IngestionTranslationKey;
        }

        if (id.Contains("metadata-linking", StringComparison.OrdinalIgnoreCase))
        {
            return MetadataLinkingKey;
        }

        return null;
    }

    private void MarkFailed(WorkflowExecution execution, string reason)
    {
        execution.Status = WorkflowStatus.Failed;
        execution.FailureReason = reason;
        execution.PendingApprovalRequest = null;
        execution.PendingCheckpoint = null;
        _sourceDocumentCache.Remove(execution.ExecutionId);
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

    private static void Touch(WorkflowExecution execution)
    {
        execution.LastUpdatedUtc = DateTimeOffset.UtcNow;
    }

    private IngestionWorkflowStatusResponse ToResponse(WorkflowExecution execution)
    {
        _store.Save(execution);

        return new IngestionWorkflowStatusResponse
        {
            ExecutionId = execution.ExecutionId,
            SourceId = execution.CorrelationId,
            Status = execution.Status.ToString(),
            AgentOutputs = new IngestionAgentOutputsResponse
            {
                IngestionTranslation = GetOutput(execution, IngestionTranslationKey),
                MetadataLinking = GetOutput(execution, MetadataLinkingKey)
            },
            FailureReason = execution.FailureReason,
            LastUpdatedUtc = execution.LastUpdatedUtc
        };
    }

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
