namespace CohereRndKnowledge.AgentProvisioning.Models;

public sealed class AgentAssetBundle
{
    public required AgentManifest Manifest { get; init; }

    public required string Instructions { get; init; }

    public required string OutputSchemaJson { get; init; }

    public required McpDeclaration Mcp { get; init; }

    public required string GovernancePolicyYaml { get; init; }

    public required string GovernanceRogueYaml { get; init; }
}
