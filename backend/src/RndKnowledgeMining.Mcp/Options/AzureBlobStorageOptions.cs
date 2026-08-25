namespace RndKnowledgeMining.Mcp.Options;

public sealed class AzureBlobStorageOptions
{
    public const string SectionName = "AzureStorage";

    public string ConnectionString { get; set; } = string.Empty;

    public string BlobServiceUri { get; set; } = string.Empty;

    public string ContainerName { get; set; } = "rd-knowledge-documents";

    public string NormalizedPrefix { get; set; } = "normalized";
}
