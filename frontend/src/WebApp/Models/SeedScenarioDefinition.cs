using Cohere.AgenticRDKnowledge.Shared.Contracts;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Studies;

namespace Cohere.AgenticRDKnowledge.WebApp.Models;

public sealed record SeedScenarioDefinition(
    string ScenarioId,
    string Block,
    string Title,
    string Description,
    string StudyId,
    string SourceId,
    string? SampleQuestion,
    string OutcomeHint,
    StudySummary Study,
    string? CaseFolder = null,
    string? FinalOutcome = null);

public static class ScenarioBackendPayload
{
    public static string DescribeStartPayload(SeedScenarioDefinition scenario)
    {
        if (scenario.Block.Equals("Ingestion", StringComparison.OrdinalIgnoreCase))
        {
            return $"POST /api/rd-knowledge/ingestion/start → {{ \"sourceId\": \"{scenario.SourceId}\" }}";
        }

        var sessionId = $"query-{scenario.ScenarioId}";
        var question = scenario.SampleQuestion ?? "(user question)";
        return $"POST /api/rd-knowledge/query/ask → {{ \"sessionId\": \"{sessionId}\", \"question\": \"{Truncate(question, 80)}\" }}";
    }

    public static IReadOnlyList<string> UiOnlyFields { get; } =
    [
        "title",
        "description",
        "studyId",
        "scenarioId",
        "outcomeHint",
        "finalOutcome",
        "study (compound, phase, endpoints)"
    ];

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "…");
}

public static class ScenarioOutcomeUi
{
    public static string FormatFinalOutcome(string finalOutcome) =>
        finalOutcome switch
        {
            "approved_persisted" => "Approved Persisted",
            "needs_human_review" => "Needs Human Review",
            "approved_with_required_labeling" => "Approved With Required Labeling",
            "denied_not_persisted" => "Denied Not Persisted",
            "no_grounded_answer" => "No Grounded Answer",
            "answer_with_citations" => "Answer With Citations",
            "insufficient_data_not_persisted" => "Insufficient Data Not Persisted",
            "approved_with_exclusions_persisted" => "Approved With Exclusions Persisted",
            "flagged_for_compliance_review" => "Flagged For Compliance Review",
            "clarification_needed" => "Clarification Needed",
            _ => ToTitleWords(finalOutcome)
        };

    private static string ToTitleWords(string snakeCase) =>
        string.Join(
            ' ',
            snakeCase.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
}

public sealed class ScenarioPickerFilter
{
    public static IReadOnlyList<SeedScenarioDefinition> Apply(
        IReadOnlyList<SeedScenarioDefinition> scenarios,
        string searchQuery,
        string? outcomeFilter)
    {
        var query = searchQuery.Trim();
        IEnumerable<SeedScenarioDefinition> filtered = scenarios;

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(s =>
                s.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.StudyId.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(outcomeFilter))
        {
            filtered = filtered.Where(s =>
                s.OutcomeHint.Equals(outcomeFilter, StringComparison.OrdinalIgnoreCase));
        }

        return filtered.ToList();
    }
}
