namespace CohereRndKnowledgeMining.Api.Host.Workflow;

/// <summary>
/// Classifies transient Foundry agent failures that are safe to retry.
/// </summary>
internal static class FoundryAgentErrors
{
    public static bool IsRetryable(Exception ex) =>
        IsRetryableMessage(ex.Message) || (ex.InnerException is not null && IsRetryable(ex.InnerException));

    public static bool IsRetryableMessage(string? message) =>
        !string.IsNullOrWhiteSpace(message)
        && message.Contains("server_error", StringComparison.OrdinalIgnoreCase);
}
