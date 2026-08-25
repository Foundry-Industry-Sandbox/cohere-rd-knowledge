using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CohereRndKnowledgeMining.Api.Host.Workflow;

internal static class WorkflowTextExtractor
{
    public static string FromAgentResponseBasic(AgentResponse response)
    {
        return response.Messages?
            .LastOrDefault(m => m.Role == ChatRole.Assistant)?
            .Text?
            .Trim()
            ?? string.Empty;
    }

    public static string FromAgentResponse(AgentResponse response)
    {
        if (response.Messages is { Count: > 0 })
        {
            return FromChatMessages(response.Messages);
        }

        return response.ToString() ?? string.Empty;
    }

    public static string GetAgentResponseText(AgentResponse response)
    {
        string finalText = GetFinalAgentTextResponse(response);
        return string.IsNullOrWhiteSpace(finalText)
            ? FromAgentResponseBasic(response)
            : finalText;
    }

    /// <summary>
    /// Returns the last assistant message text from a completed agent response.
    /// </summary>
    public static string GetFinalAgentTextResponse(AgentResponse response)
    {
        if (response.Messages is not { Count: > 0 })
        {
            return string.Empty;
        }

        foreach (ChatMessage message in response.Messages.Reverse())
        {
            if (message.Role != ChatRole.Assistant)
            {
                continue;
            }

            string? text = GetAssistantTextOnly(message);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return string.Empty;
    }

    private static string? GetAssistantTextOnly(ChatMessage message)
    {
        if (message.Contents is null || message.Contents.Count == 0)
        {
            return string.IsNullOrWhiteSpace(message.Text) ? null : message.Text.Trim();
        }

        List<string> textParts = [];
        foreach (AIContent content in message.Contents)
        {
            if (content is TextContent textContent && !string.IsNullOrWhiteSpace(textContent.Text))
            {
                textParts.Add(textContent.Text.Trim());
            }
        }

        return textParts.Count == 0 ? null : string.Join('\n', textParts).Trim();
    }

    public static string FromChatMessages(IEnumerable<ChatMessage> messages)
    {
        var builder = new StringBuilder();

        foreach (ChatMessage message in messages)
        {
            AppendMessageContent(builder, message);
        }

        return builder.ToString().Trim();
    }

    public static string FromLastAssistantMessage(IEnumerable<ChatMessage> messages)
    {
        foreach (ChatMessage message in messages.Reverse())
        {
            if (message.Role != ChatRole.Assistant)
            {
                continue;
            }

            string text = FromMessage(message);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return ExtractLastAssistantBlock(FromChatMessages(messages));
    }

    public static bool HasAssistantText(IEnumerable<ChatMessage> messages) =>
        messages.Any(message =>
            message.Role == ChatRole.Assistant &&
            !string.IsNullOrWhiteSpace(FromMessage(message)));

    public static string FromMessage(ChatMessage message)
    {
        var builder = new StringBuilder();
        AppendPlainText(builder, message);
        return builder.ToString().Trim();
    }

    private static string ExtractLastAssistantBlock(string formatted)
    {
        const string assistantPrefix = "[assistant]";
        int assistantIndex = formatted.LastIndexOf(assistantPrefix, StringComparison.OrdinalIgnoreCase);
        if (assistantIndex < 0)
        {
            return formatted.Trim();
        }

        return formatted[(assistantIndex + assistantPrefix.Length)..].TrimStart();
    }

    private static void AppendMessageContent(StringBuilder builder, ChatMessage message)
    {
        string messageText = message.Text;
        if (!string.IsNullOrWhiteSpace(messageText))
        {
            builder.Append('[')
                .Append(message.Role)
                .Append("] ")
                .AppendLine(messageText);
        }

        if (message.Contents is null)
        {
            if (string.IsNullOrWhiteSpace(messageText))
            {
                builder.Append('[')
                    .Append(message.Role)
                    .Append("] ")
                    .AppendLine();
            }

            return;
        }

        bool wroteRolePrefix = !string.IsNullOrWhiteSpace(messageText);
        foreach (AIContent content in message.Contents)
        {
            if (content is ErrorContent errorContent)
            {
                if (!wroteRolePrefix)
                {
                    builder.Append('[')
                        .Append(message.Role)
                        .Append("] ");
                    wroteRolePrefix = true;
                }

                builder
                    .Append("Error")
                    .Append(string.IsNullOrWhiteSpace(errorContent.ErrorCode) ? string.Empty : $" ({errorContent.ErrorCode})")
                    .Append(": ")
                    .AppendLine(errorContent.Message);

                if (!string.IsNullOrWhiteSpace(errorContent.Details))
                {
                    builder.AppendLine(errorContent.Details);
                }

                continue;
            }

            if (content is TextContent textContent && !string.IsNullOrWhiteSpace(textContent.Text))
            {
                if (!wroteRolePrefix)
                {
                    builder.Append('[')
                        .Append(message.Role)
                        .Append("] ");
                    wroteRolePrefix = true;
                }

                builder.AppendLine(textContent.Text);
                continue;
            }

            string? rendered = content.ToString();
            if (string.IsNullOrWhiteSpace(rendered))
            {
                continue;
            }

            if (!wroteRolePrefix)
            {
                builder.Append('[')
                    .Append(message.Role)
                    .Append("] ");
                wroteRolePrefix = true;
            }

            builder.AppendLine(rendered);
        }

        if (!wroteRolePrefix)
        {
            builder.Append('[')
                .Append(message.Role)
                .Append("] ")
                .AppendLine();
        }
    }

    private static void AppendPlainText(StringBuilder builder, ChatMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.Text))
        {
            builder.AppendLine(message.Text);
        }

        if (message.Contents is null)
        {
            return;
        }

        foreach (AIContent content in message.Contents)
        {
            if (content is TextContent textContent && !string.IsNullOrWhiteSpace(textContent.Text))
            {
                if (string.Equals(textContent.Text.Trim(), message.Text?.Trim(), StringComparison.Ordinal))
                {
                    continue;
                }

                builder.AppendLine(textContent.Text);
                continue;
            }

            if (content is ErrorContent errorContent)
            {
                builder
                    .Append("Error")
                    .Append(string.IsNullOrWhiteSpace(errorContent.ErrorCode) ? string.Empty : $" ({errorContent.ErrorCode})")
                    .Append(": ")
                    .AppendLine(errorContent.Message);

                if (!string.IsNullOrWhiteSpace(errorContent.Details))
                {
                    builder.AppendLine(errorContent.Details);
                }
            }
        }
    }
}
