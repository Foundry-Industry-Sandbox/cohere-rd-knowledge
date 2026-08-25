namespace RndKnowledgeMining.Mcp.Options;

public sealed class McpStartupOptions
{
    public const string SectionName = "McpStartup";

    public bool EnsureSearchIndexesOnStartup { get; set; } = true;

    public bool SeedPoliciesOnStartup { get; set; } = false;
}
