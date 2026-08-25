using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using RndKnowledgeMining.Mcp.Adapters;

namespace CohereRndKnowledgeMining.Api.Host.Workflow;

/// <summary>
/// Estimates token-related sizes for workflow agent turns. Logs only when thresholds are exceeded.
/// </summary>
internal static class WorkflowTokenDiagnostics
{
    private const int WarnEstimatedInputTokens = 12_000;
    private const int WarnEstimatedOutputTokens = 6_000;
    private const int WarnPerDocumentPreparedTokens = 2_500;

    public static void LogMetadataLinkingHandoff(
        ILogger logger,
        string sourceId,
        string executionId,
        ChatMessage handoffMessage)
    {
        int messageChars = CountMessageChars(handoffMessage);
        int estTokens = EstimateTokens(messageChars);
        if (estTokens < WarnEstimatedInputTokens)
        {
            logger.LogDebug(
                "Metadata-linking handoff for {SourceId} execution {ExecutionId}: estMessageTokens={EstMessageTokens}",
                sourceId,
                executionId,
                estTokens);
            return;
        }

        logger.LogWarning(
            "Metadata-linking handoff for {SourceId} execution {ExecutionId}: messageChars={MessageChars}, estMessageTokens={EstMessageTokens}",
            sourceId,
            executionId,
            messageChars,
            estTokens);
    }

    public static void LogInlineIngestionPayload(
        ILogger logger,
        string sourceId,
        string executionId,
        IReadOnlyList<Services.RawKnowledgeItem> items,
        IList<ChatMessage> messages)
    {
        int totalRawChars = 0;
        int totalPreparedChars = 0;

        foreach (Services.RawKnowledgeItem item in items)
        {
            string raw = item.Content ?? string.Empty;
            string prepared = RawDocumentContentPreparer.PrepareForAgent(item.Title, raw);
            totalRawChars += raw.Length;
            totalPreparedChars += prepared.Length;

            int estPreparedTokens = EstimateTokens(prepared.Length);
            if (estPreparedTokens >= WarnPerDocumentPreparedTokens)
            {
                logger.LogWarning(
                    "Large ingestion document {SourceId}/{FileName}: rawChars={RawChars}, preparedChars={PreparedChars}, estPreparedTokens={EstPreparedTokens}, sourceType={SourceType}",
                    sourceId,
                    item.Title,
                    raw.Length,
                    prepared.Length,
                    estPreparedTokens,
                    item.SourceType);
            }
        }

        int messageChars = SumMessageChars(messages);
        int estMessageTokens = EstimateTokens(messageChars);
        if (estMessageTokens >= WarnEstimatedInputTokens)
        {
            logger.LogWarning(
                "Large ingestion inline payload for {SourceId} execution {ExecutionId}: documents={DocumentCount}, totalPreparedChars={TotalPreparedChars}, estPreparedTokens={EstPreparedTokens}, messageChars={MessageChars}, estMessageTokens={EstMessageTokens}",
                sourceId,
                executionId,
                items.Count,
                totalPreparedChars,
                EstimateTokens(totalPreparedChars),
                messageChars,
                estMessageTokens);
            return;
        }

        logger.LogInformation(
            "Ingestion inline payload for {SourceId} execution {ExecutionId}: documents={DocumentCount}, estMessageTokens={EstMessageTokens}",
            sourceId,
            executionId,
            items.Count,
            estMessageTokens);
    }

    public static void LogAgentTurn(
        ILogger logger,
        string executorId,
        AgentResponse? response,
        bool isFinal)
    {
        if (response is null || !isFinal)
        {
            return;
        }

        int outputChars = CountResponseTextChars(response);
        int estOutputTokens = EstimateTokens(outputChars);
        int toolCallCount = CountToolCalls(response);
        UsageSnapshot? usage = TryReadUsage(response);
        bool hasError = ContainsAgentError(response);

        if (!hasError
            && estOutputTokens < WarnEstimatedOutputTokens
            && usage?.TotalTokens is null or < WarnEstimatedInputTokens)
        {
            logger.LogDebug(
                "Agent turn {ExecutorId} completed: estOutputTokens={EstOutputTokens}, toolCalls={ToolCallCount}",
                executorId,
                estOutputTokens,
                toolCallCount);
            return;
        }

        if (usage is not null)
        {
            logger.LogWarning(
                "Agent turn {ExecutorId} completed: outputChars={OutputChars}, estOutputTokens={EstOutputTokens}, toolCalls={ToolCallCount}, inputTokens={InputTokens}, outputTokens={OutputTokens}, totalTokens={TotalTokens}, hasError={HasError}",
                executorId,
                outputChars,
                estOutputTokens,
                toolCallCount,
                usage.InputTokens,
                usage.OutputTokens,
                usage.TotalTokens,
                hasError);
            return;
        }

        logger.LogWarning(
            "Agent turn {ExecutorId} completed: outputChars={OutputChars}, estOutputTokens={EstOutputTokens}, toolCalls={ToolCallCount}, hasError={HasError}",
            executorId,
            outputChars,
            estOutputTokens,
            toolCallCount,
            hasError);
    }

    public static void LogExecutorFailure(
        ILogger logger,
        string executorId,
        string message,
        int? lastOutputChars = null)
    {
        logger.LogError(
            "Executor {ExecutorId} failed: {FailureMessage}. LastKnownOutputChars={LastOutputChars}, estLastOutputTokens={EstLastOutputTokens}",
            executorId,
            message,
            lastOutputChars,
            lastOutputChars is int chars ? EstimateTokens(chars) : null);
    }

    private static bool ContainsAgentError(AgentResponse response)
    {
        if (response.Messages is null)
        {
            return false;
        }

        foreach (ChatMessage message in response.Messages)
        {
            if (message.Contents is null)
            {
                continue;
            }

            foreach (AIContent content in message.Contents)
            {
                if (content is ErrorContent)
                {
                    return true;
                }
            }
        }

        string text = CountResponseTextChars(response) > 0
            ? WorkflowTextExtractor.GetAgentResponseText(response)
            : string.Empty;

        return text.Contains("Error (", StringComparison.OrdinalIgnoreCase);
    }

    private static int SumMessageChars(IEnumerable<ChatMessage> messages) =>
        messages.Sum(CountMessageChars);

    private static int CountResponseTextChars(AgentResponse response)
    {
        if (response.Messages is not { Count: > 0 })
        {
            return response.ToString()?.Length ?? 0;
        }

        return SumMessageChars(response.Messages);
    }

    private static int CountMessageChars(ChatMessage message)
    {
        int chars = message.Text?.Length ?? 0;

        if (message.Contents is null)
        {
            return chars;
        }

        foreach (AIContent content in message.Contents)
        {
            switch (content)
            {
                case TextContent textContent when !string.IsNullOrEmpty(textContent.Text):
                    if (!string.Equals(textContent.Text, message.Text, StringComparison.Ordinal))
                    {
                        chars += textContent.Text.Length;
                    }

                    break;
                case FunctionResultContent functionResult when functionResult.Result is not null:
                    chars += functionResult.Result.ToString()?.Length ?? 0;
                    break;
                case ErrorContent errorContent:
                    chars += (errorContent.Message?.Length ?? 0) + (errorContent.Details?.Length ?? 0);
                    break;
                default:
                    chars += content.ToString()?.Length ?? 0;
                    break;
            }
        }

        return chars;
    }

    private static int CountToolCalls(AgentResponse response) =>
        response.Messages?.Sum(CountToolCalls) ?? 0;

    private static int CountToolCalls(ChatMessage message)
    {
        if (message.Contents is null || message.Contents.Count == 0)
        {
            return 0;
        }

        return message.Contents.Count(content =>
            content is FunctionCallContent
            || content is FunctionResultContent);
    }

    private static UsageSnapshot? TryReadUsage(AgentResponse response)
    {
        UsageSnapshot? usage = null;
        if (response.Messages is null)
        {
            return null;
        }

        foreach (ChatMessage message in response.Messages)
        {
            usage = MergeUsage(usage, TryReadUsage(message));
        }

        return usage;
    }

    private static UsageSnapshot? TryReadUsage(ChatMessage message)
    {
        if (message.Contents is null)
        {
            return null;
        }

        UsageSnapshot? usage = null;
        foreach (AIContent content in message.Contents)
        {
            if (content is not UsageContent usageContent)
            {
                continue;
            }

            usage = MergeUsage(usage, ToSnapshot(usageContent.Details));
        }

        return usage;
    }

    private static UsageSnapshot? ToSnapshot(UsageDetails? details)
    {
        if (details is null)
        {
            return null;
        }

        return new UsageSnapshot(
            details.InputTokenCount,
            details.OutputTokenCount,
            details.TotalTokenCount);
    }

    private static UsageSnapshot? MergeUsage(UsageSnapshot? left, UsageSnapshot? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return new UsageSnapshot(
            AddNullable(left.InputTokens, right.InputTokens),
            AddNullable(left.OutputTokens, right.OutputTokens),
            AddNullable(left.TotalTokens, right.TotalTokens));
    }

    private static long? AddNullable(long? left, long? right)
    {
        if (left is null && right is null)
        {
            return null;
        }

        return (left ?? 0) + (right ?? 0);
    }

    private static int EstimateTokens(int charCount) =>
        charCount <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(charCount / 4.0));

    private sealed record UsageSnapshot(long? InputTokens, long? OutputTokens, long? TotalTokens);
}
