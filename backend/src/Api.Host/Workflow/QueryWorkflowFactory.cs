using CohereRndKnowledgeMining.Api.Host.Services;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using AgentWorkflow = Microsoft.Agents.AI.Workflows.Workflow;

namespace CohereRndKnowledgeMining.Api.Host.Workflow;

public static class QueryWorkflowConstants
{
    public const string SharedStateScope = "QueryCurateWorkflowState";

    public const string PendingMessagesKey = "PendingMessages";

    public const string PendingCurationResultKey = "PendingCurationResult";

    public const string ApprovalPortId = "QueryComplianceApproval";

    public const string ReviewerRole = "compliance-reviewer";

    public const string CurationComplianceAgentName = "curation-compliance-agent";
}

/// <summary>
/// Block 2 (Query) Curate workflow: runs the curation-compliance agent over all accumulated
/// Search &amp; Chat responses, then pauses at the Compliance Reviewer gate. On approval the
/// curation result is yielded as the workflow output.
/// Note: this factory only models the on-demand Curate process. The interactive Search &amp; Chat
/// loop is handled directly by <see cref="QueryWorkflowService"/> via direct agent calls.
/// </summary>
public sealed class QueryWorkflowFactory
{
    public AgentWorkflow CreateWorkflow(
        AIAgent curationComplianceAgent,
        string sessionId,
        string executionId)
    {
        RequestPort approvalPort = RequestPort.Create<HumanApprovalPrompt, HumanApprovalDecision>(
            QueryWorkflowConstants.ApprovalPortId);

        var agentHostOptions = new AIAgentHostOptions
        {
            EmitAgentResponseEvents = true,
            ForwardIncomingMessages = false
        };

        var curationCompliance = curationComplianceAgent.BindAsExecutor(agentHostOptions);

        FunctionExecutor<IList<ChatMessage>> requestComplianceApproval = CreateComplianceApprovalRequestExecutor(
            id: "QueryComplianceApprovalRequest",
            correlationId: sessionId,
            executionId: executionId);
        FunctionExecutor<HumanApprovalDecision> applyComplianceDecision = CreateComplianceApprovalDecisionExecutor(
            id: "QueryComplianceApprovalDecision",
            correlationId: sessionId,
            executionId: executionId);

        return new WorkflowBuilder(curationCompliance)
            .AddEdge(curationCompliance, requestComplianceApproval)
            .AddEdge(requestComplianceApproval, approvalPort)
            .AddEdge(approvalPort, applyComplianceDecision)
            .WithOutputFrom(applyComplianceDecision)
            .WithName($"rd-curate-{executionId}")
            .WithDescription("Block 2 curate workflow with a Compliance Reviewer approval gate over the chat responses.")
            .Build();
    }

    private static FunctionExecutor<IList<ChatMessage>> CreateComplianceApprovalRequestExecutor(
        string id,
        string correlationId,
        string executionId)
    {
        return new FunctionExecutor<IList<ChatMessage>>(
            id: id,
            handlerAsync: async (messages, context, cancellationToken) =>
            {
                string rawOutput = WorkflowTextExtractor.FromLastAssistantMessage(messages);
                AgentStepResult result = ParseBridgeOutput(QueryWorkflowConstants.CurationComplianceAgentName, rawOutput);
                ChatMessage payload = WorkflowPayloadBuilder.CreateAgentTransitionMessage(
                    correlationId,
                    executionId,
                    result);

                await context.QueueStateUpdateAsync(
                    QueryWorkflowConstants.PendingCurationResultKey,
                    result,
                    scopeName: QueryWorkflowConstants.SharedStateScope,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                await context.QueueStateUpdateAsync(
                    QueryWorkflowConstants.PendingMessagesKey,
                    new List<ChatMessage> { payload },
                    scopeName: QueryWorkflowConstants.SharedStateScope,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                var prompt = new HumanApprovalPrompt
                {
                    CorrelationId = correlationId,
                    ExecutionId = executionId,
                    ReviewerRole = QueryWorkflowConstants.ReviewerRole,
                    Summary = "Curation and compliance review completed. Approve to finalize the flags and captured decisions.",
                    ReviewedOutput = rawOutput
                };

                await context.SendMessageAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
            },
            sentMessageTypes: [typeof(HumanApprovalPrompt)]);
    }

    private static FunctionExecutor<HumanApprovalDecision> CreateComplianceApprovalDecisionExecutor(
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
                    throw new InvalidOperationException("Curate workflow was denied by the compliance reviewer.");
                }

                AgentStepResult? pendingResult = await context
                    .ReadStateAsync<AgentStepResult>(
                        QueryWorkflowConstants.PendingCurationResultKey,
                        scopeName: QueryWorkflowConstants.SharedStateScope,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (pendingResult is null)
                {
                    throw new InvalidOperationException("Pending curation result was not available when resuming the curate workflow.");
                }

                ChatMessage curationResult = WorkflowPayloadBuilder.CreateAgentTransitionMessage(
                    correlationId,
                    executionId,
                    pendingResult);

                await context.YieldOutputAsync(curationResult.Text, cancellationToken).ConfigureAwait(false);
            },
            sentMessageTypes: []);
    }

    private static AgentStepResult ParseBridgeOutput(string sourceAgentName, string rawOutput) =>
        AgentStructuredOutputParser.Parse(sourceAgentName, rawOutput);
}
