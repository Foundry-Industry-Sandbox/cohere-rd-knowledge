using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CohereRndKnowledgeMining.Api.Host.Workflow;

/// <summary>
/// Invokes metadata-linking via <see cref="AIAgent.RunAsync"/> with retries on transient Foundry server_error responses.
/// Keeps the slim storage-pointer handoff while avoiding workflow failure on a single transient API error.
/// </summary>
internal static class MetadataLinkingRetryExecutor
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan RetryDelayBase = TimeSpan.FromSeconds(2);

    public static FunctionExecutor<ChatMessage, ChatMessage[]> Create(AIAgent metadataLinkingAgent, ILogger logger)
    {
        return new FunctionExecutor<ChatMessage, ChatMessage[]>(
            id: IngestionWorkflowConstants.MetadataLinkingAgentName,
            handlerAsync: async (handoffMessage, context, cancellationToken) =>
            {
                Exception? lastFailure = null;

                for (int attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    try
                    {
                        AgentResponse response = await metadataLinkingAgent
                            .RunAsync([handoffMessage], cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        WorkflowTokenDiagnostics.LogAgentTurn(
                            logger,
                            IngestionWorkflowConstants.MetadataLinkingAgentName,
                            response,
                            isFinal: true);

                        string rawOutput = WorkflowTextExtractor.GetAgentResponseText(response);
                        if (FoundryAgentErrors.IsRetryableMessage(rawOutput))
                        {
                            throw new InvalidOperationException(
                                $"Agent '{IngestionWorkflowConstants.MetadataLinkingAgentName}' returned a transient Foundry error: {rawOutput}");
                        }

                        AgentStructuredOutputParser.Parse(
                            IngestionWorkflowConstants.MetadataLinkingAgentName,
                            rawOutput);

                        ChatMessage[] messages = [new ChatMessage(ChatRole.Assistant, rawOutput)];

                        await context.SendMessageAsync(messages, cancellationToken: cancellationToken)
                            .ConfigureAwait(false);
                        await context.SendMessageAsync(new TurnToken(emitEvents: true), cancellationToken: cancellationToken)
                            .ConfigureAwait(false);
                        return messages;
                    }
                    catch (Exception ex) when (FoundryAgentErrors.IsRetryable(ex) && attempt < MaxAttempts)
                    {
                        lastFailure = ex;
                        logger.LogWarning(
                            ex,
                            "metadata-linking-agent transient Foundry failure on attempt {Attempt}/{MaxAttempts}; retrying.",
                            attempt,
                            MaxAttempts);
                        await Task.Delay(RetryDelayBase * attempt, cancellationToken).ConfigureAwait(false);
                    }
                }

                throw lastFailure ?? new InvalidOperationException(
                    $"metadata-linking-agent failed after {MaxAttempts} attempts.");
            },
            sentMessageTypes: [typeof(ChatMessage[]), typeof(TurnToken)]);
    }
}
