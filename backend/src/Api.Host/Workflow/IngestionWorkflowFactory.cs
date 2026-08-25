using CohereRndKnowledgeMining.Api.Host.Services;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using RndKnowledgeMining.Mcp.Adapters;
using AgentWorkflow = Microsoft.Agents.AI.Workflows.Workflow;

namespace CohereRndKnowledgeMining.Api.Host.Workflow;

public static class IngestionWorkflowConstants
{
    public const string SharedStateScope = "IngestionWorkflowState";

    public const string PendingMessagesKey = "PendingMessages";

    public const string PendingMetadataResultKey = "PendingMetadataResult";

    public const string ApprovalPortId = "IngestionCuratorApproval";

    public const string ReviewerRole = "knowledge-curator";

    public const string IngestionTranslationAgentName = "ingestion-translation-agent";

    public const string MetadataLinkingAgentName = "metadata-linking-agent";

    public const string IngestionTranslationOutputKey = "IngestionTranslation";

    public const string MetadataLinkingOutputKey = "MetadataLinking";
}

/// <summary>
/// Block 1 (Ingestion) workflow: ingestion-translation -> metadata-linking -> Knowledge Curator gate.
/// Metadata & Linking indexes into the Vector DB via MCP before the gate; curator approve/deny closes the run.
/// </summary>
public sealed class IngestionWorkflowFactory
{
    private readonly INormalizedDocumentStore _normalizedDocumentStore;
    private readonly IngestionSourceDocumentCache _sourceDocumentCache;
    private readonly ILogger<IngestionWorkflowFactory> _logger;

    public IngestionWorkflowFactory(
        INormalizedDocumentStore normalizedDocumentStore,
        IngestionSourceDocumentCache sourceDocumentCache,
        ILogger<IngestionWorkflowFactory> logger)
    {
        _normalizedDocumentStore = normalizedDocumentStore;
        _sourceDocumentCache = sourceDocumentCache;
        _logger = logger;
    }

    public AgentWorkflow CreateWorkflow(
        AIAgent ingestionTranslationAgent,
        AIAgent metadataLinkingAgent,
        string sourceId,
        string executionId)
    {
        RequestPort approvalPort = RequestPort.Create<HumanApprovalPrompt, HumanApprovalDecision>(
            IngestionWorkflowConstants.ApprovalPortId);

        var agentHostOptions = new AIAgentHostOptions
        {
            EmitAgentResponseEvents = true,
            ForwardIncomingMessages = false
        };

        var ingestionTranslation = ingestionTranslationAgent.BindAsExecutor(agentHostOptions);
        var metadataLinking = MetadataLinkingRetryExecutor.Create(metadataLinkingAgent, _logger);

        FunctionExecutor<IList<ChatMessage>> bridge01 = CreateIngestionTranslationBridgeExecutor(
            id: "IngestionBridge01",
            correlationId: sourceId,
            executionId: executionId);
        FunctionExecutor<IList<ChatMessage>> bridge02 = CreateAgentHandoffBridgeExecutor(
            id: "IngestionBridge02",
            correlationId: sourceId,
            executionId: executionId,
            sourceAgentName: IngestionWorkflowConstants.MetadataLinkingAgentName);
        FunctionExecutor<ChatMessage> requestCuratorApproval = CreateCuratorApprovalRequestExecutor(
            id: "IngestionCuratorApprovalRequest",
            correlationId: sourceId,
            executionId: executionId);
        FunctionExecutor<HumanApprovalDecision> applyCuratorDecision = CreateCuratorApprovalDecisionExecutor(
            id: "IngestionCuratorApprovalDecision",
            correlationId: sourceId,
            executionId: executionId);

        return new WorkflowBuilder(ingestionTranslation)
            .AddEdge(ingestionTranslation, bridge01)
            .AddEdge(bridge01, metadataLinking)
            .AddEdge(metadataLinking, bridge02)
            .AddEdge(bridge02, requestCuratorApproval)
            .AddEdge(requestCuratorApproval, approvalPort)
            .AddEdge(approvalPort, applyCuratorDecision)
            .WithOutputFrom(applyCuratorDecision)
            .WithName($"rd-ingestion-{executionId}")
            .WithDescription("Block 1 ingestion workflow: metadata-linking indexes to Vector DB, then Knowledge Curator reviews.")
            .Build();
    }

    private FunctionExecutor<IList<ChatMessage>> CreateIngestionTranslationBridgeExecutor(
        string id,
        string correlationId,
        string executionId)
    {
        return new FunctionExecutor<IList<ChatMessage>>(
            id: id,
            handlerAsync: async (messages, context, cancellationToken) =>
            {
                string rawOutput = WorkflowTextExtractor.FromLastAssistantMessage(messages);
                AgentStepResult result = ParseBridgeOutput(
                    IngestionWorkflowConstants.IngestionTranslationAgentName,
                    rawOutput);

                if (string.IsNullOrWhiteSpace(result.RawPayloadJson))
                {
                    throw new InvalidOperationException(
                        "Ingestion handoff is missing raw payload JSON.");
                }

                string payloadJson = result.RawPayloadJson;
                if (_sourceDocumentCache.TryGet(executionId, out IReadOnlyList<RawKnowledgeItem>? sourceItems)
                    && sourceItems is not null)
                {
                    var preparedItems = sourceItems
                        .Select(i => new RawKnowledgeItem
                        {
                            ItemId = i.ItemId,
                            Title = i.Title,
                            SourceType = i.SourceType,
                            SourcePath = i.SourcePath,
                            Content = RawDocumentContentPreparer.PrepareForAgent(i.Title, i.Content)
                        })
                        .ToList();
                    payloadJson = IngestionHandoffEnricher.Enrich(payloadJson, preparedItems);
                }

                string manifestJson = await _normalizedDocumentStore
                    .PersistIngestionHandoffAsync(
                        correlationId,
                        executionId,
                        payloadJson,
                        cancellationToken)
                    .ConfigureAwait(false);

                ChatMessage payload = WorkflowPayloadBuilder.CreateMetadataLinkingHandoffMessage(
                    correlationId,
                    executionId,
                    manifestJson);

                WorkflowTokenDiagnostics.LogMetadataLinkingHandoff(
                    _logger,
                    correlationId,
                    executionId,
                    payload);

                await context.SendMessageAsync(payload, cancellationToken: cancellationToken).ConfigureAwait(false);
                await context.SendMessageAsync(new TurnToken(emitEvents: true), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            },
            sentMessageTypes: [typeof(ChatMessage), typeof(TurnToken)]);
    }

    private static FunctionExecutor<IList<ChatMessage>> CreateAgentHandoffBridgeExecutor(
        string id,
        string correlationId,
        string executionId,
        string sourceAgentName)
    {
        return new FunctionExecutor<IList<ChatMessage>>(
            id: id,
            handlerAsync: async (messages, context, cancellationToken) =>
            {
                string rawOutput = WorkflowTextExtractor.FromLastAssistantMessage(messages);
                AgentStepResult result = ParseBridgeOutput(sourceAgentName, rawOutput);
                ChatMessage payload = WorkflowPayloadBuilder.CreateRichAgentHandoffMessage(
                    correlationId,
                    executionId,
                    sourceAgentName,
                    result);

                await context.SendMessageAsync(payload, cancellationToken: cancellationToken).ConfigureAwait(false);
                await context.SendMessageAsync(new TurnToken(emitEvents: true), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            },
            sentMessageTypes: [typeof(ChatMessage), typeof(TurnToken)]);
    }

    private static FunctionExecutor<ChatMessage> CreateCuratorApprovalRequestExecutor(
        string id,
        string correlationId,
        string executionId)
    {
        return new FunctionExecutor<ChatMessage>(
            id: id,
            handlerAsync: async (message, context, cancellationToken) =>
            {
                string rawOutput = WorkflowTextExtractor.FromMessage(message);
                AgentStepResult result = ParseBridgeOutput(IngestionWorkflowConstants.MetadataLinkingAgentName, rawOutput);
                ChatMessage payload = WorkflowPayloadBuilder.CreateRichAgentHandoffMessage(
                    correlationId,
                    executionId,
                    IngestionWorkflowConstants.MetadataLinkingAgentName,
                    result);

                await context.QueueStateUpdateAsync(
                    IngestionWorkflowConstants.PendingMetadataResultKey,
                    result,
                    scopeName: IngestionWorkflowConstants.SharedStateScope,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                await context.QueueStateUpdateAsync(
                    IngestionWorkflowConstants.PendingMessagesKey,
                    new List<ChatMessage> { payload },
                    scopeName: IngestionWorkflowConstants.SharedStateScope,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                var prompt = new HumanApprovalPrompt
                {
                    CorrelationId = correlationId,
                    ExecutionId = executionId,
                    ReviewerRole = IngestionWorkflowConstants.ReviewerRole,
                    Summary = "Metadata linking and Vector DB indexing completed. Review linking quality and approve or deny the ingestion run.",
                    ReviewedOutput = rawOutput
                };

                await context.SendMessageAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
            },
            sentMessageTypes: [typeof(HumanApprovalPrompt)]);
    }

    private static FunctionExecutor<HumanApprovalDecision> CreateCuratorApprovalDecisionExecutor(
        string id,
        string correlationId,
        string executionId)
    {
        return new FunctionExecutor<HumanApprovalDecision>(
            id: id,
            handlerAsync: async (decision, context, cancellationToken) =>
            {
                if (!decision.Approved)
                {
                    throw new InvalidOperationException("Ingestion workflow was denied by the knowledge curator.");
                }

                AgentStepResult? pendingResult = await context
                    .ReadStateAsync<AgentStepResult>(
                        IngestionWorkflowConstants.PendingMetadataResultKey,
                        scopeName: IngestionWorkflowConstants.SharedStateScope,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (pendingResult is null)
                {
                    throw new InvalidOperationException("Pending metadata-linking result was not available when resuming the ingestion workflow.");
                }

                ChatMessage curatedKnowledge = new(
                    ChatRole.Assistant,
                    pendingResult.RawPayloadJson ?? string.Empty);

                await context.YieldOutputAsync(curatedKnowledge.Text, cancellationToken).ConfigureAwait(false);
            },
            outputTypes: [typeof(string)]);
    }

    private static AgentStepResult ParseBridgeOutput(string sourceAgentName, string rawOutput) =>
        AgentStructuredOutputParser.Parse(sourceAgentName, rawOutput);
}
