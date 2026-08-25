namespace RndKnowledgeMining.Mcp.Options;

public sealed class AzureSearchOptions
{
    public const string SectionName = "AzureSearch";

    public string Endpoint { get; set; } = string.Empty;

    public string KnowledgeIndexName { get; set; } = "rd-knowledge-evidence";

    public string PolicyIndexName { get; set; } = "rd-policy-knowledge";

    public int VectorDimensions { get; set; } = 1024;
}
