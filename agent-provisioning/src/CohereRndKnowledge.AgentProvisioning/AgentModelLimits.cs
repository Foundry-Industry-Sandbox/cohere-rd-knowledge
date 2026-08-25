namespace CohereRndKnowledge.AgentProvisioning;

internal static class AgentModelLimits
{
    private const string IngestionTranslationAgentName = "ingestion-translation-agent";

    /// <summary>
    /// Command reasoning and final output share this budget, and Foundry rate-limit
    /// estimation reserves it in addition to prompt tokens. This limit must not
    /// depend on the user-configurable deployment alias.
    /// </summary>
    private const int DefaultInstructionOnlyOutputTokenBudget = 4_096;

    private const int IngestionTranslationOutputTokenBudget = 6_144;

    public static int GetInstructionOnlyOutputTokenBudget(string agentName) =>
        string.Equals(agentName, IngestionTranslationAgentName, StringComparison.OrdinalIgnoreCase)
            ? IngestionTranslationOutputTokenBudget
            : DefaultInstructionOnlyOutputTokenBudget;
}
