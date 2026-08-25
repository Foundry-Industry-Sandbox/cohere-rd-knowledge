using CohereRndKnowledgeMining.Api.Host.Workflow;

namespace CohereRndKnowledgeMining.Api.Host.Services;

/// <summary>
/// Determines whether a Search &amp; Chat session has grounded material eligible for the Curate process.
/// Curate is disabled when all turns are empty-KB / no-grounded-answer outcomes.
/// </summary>
public static class QuerySessionCurateRules
{
    public const string EmptyKnowledgeBaseAnswer =
        "No grounded information is available yet — ingest knowledge first.";

    public const string NoGroundedResponsesToCurateMessage =
        "This session has no grounded Search & Chat responses to curate. " +
        "Ingest knowledge into the Vector DB and ask again when grounded answers are available.";

    public static bool IsCurateEnabled(QueryChatSession session) =>
        session.Turns.Any(turn => turn.IsGrounded);

    public static bool EvaluateGrounded(
        string answer,
        IReadOnlyList<string> citations,
        IReadOnlyList<RetrievedPassage> passages,
        string rawAgentOutput)
    {
        if (IsNoGroundedAnswer(answer, rawAgentOutput))
        {
            return false;
        }

        if (citations.Count > 0)
        {
            return true;
        }

        return passages.Count > 0;
    }

    public static bool IsNoGroundedAnswer(string answer, string rawAgentOutput)
    {
        if (answer.Contains(EmptyKnowledgeBaseAnswer, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        AgentStructuredOutput? structured = AgentStructuredOutputParser.TryParseStructuredOutput(rawAgentOutput);
        if (structured is null)
        {
            return false;
        }

        return string.Equals(structured.Decision, "Insufficient Evidence", StringComparison.OrdinalIgnoreCase)
            && (structured.Citations is null || structured.Citations.Count == 0);
    }
}
