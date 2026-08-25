namespace CohereRndKnowledge.AgentProvisioning.Models;

public sealed class AgentManifest
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string InstructionsFile { get; init; }

    /// <summary>
    /// Path to JSON Schema for strict Foundry output (required when <see cref="OutputFormat"/> is strict_schema).
    /// </summary>
    public string? OutputSchemaFile { get; init; }

    /// <summary>
    /// strict_schema — enforce agent-specific JSON Schema (Search &amp; Chat, Curation).
    /// instructions_only — rich JSON shape in instructions.md; no Foundry text.format (required for MCP multi-turn agents).
    /// </summary>
    public string OutputFormat { get; init; } = "strict_schema";

    public required IReadOnlyList<string> AllowedDecisions { get; init; }

    public string GovernancePolicyFile { get; init; } = "governance.yaml";

    public string GovernanceRogueFile { get; init; } = "rogue.yaml";
}
