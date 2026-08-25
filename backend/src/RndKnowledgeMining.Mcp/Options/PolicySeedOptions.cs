namespace RndKnowledgeMining.Mcp.Options;

public sealed class PolicySeedOptions
{
    public const string SectionName = "PolicySeed";

    public string PolicyFilePath { get; set; } = "../../../policies/hls_policies.txt";
}
