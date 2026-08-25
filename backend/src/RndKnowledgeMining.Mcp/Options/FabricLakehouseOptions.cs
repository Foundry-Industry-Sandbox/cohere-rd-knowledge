namespace RndKnowledgeMining.Mcp.Options;

public sealed class FabricLakehouseOptions
{
    public const string SectionName = "FabricLakehouse";

    public string WorkspaceName { get; set; } = string.Empty;

    public string LakehouseName { get; set; } = string.Empty;

    /// <summary>
    /// OneLake prefix for case ingest files. Layout mirrors local dataset-seed:
    /// <c>{EvidenceRoot}/cases/{sourceId}/ingest/{file}</c> (default <c>Files/bronze</c>).
    /// </summary>
    public string EvidenceRoot { get; set; } = "Files/bronze";

    public string NormalizedRoot { get; set; } = "Files/normalized";

    public int TimeoutSeconds { get; set; } = 30;
}
