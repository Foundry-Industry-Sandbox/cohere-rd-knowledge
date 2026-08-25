namespace RndKnowledgeMining.Mcp.Options;

public sealed class AzureFoundryModelsOptions
{
    public const string SectionName = "AzureFoundryModels";

    public string EmbedDeploymentName { get; set; } = "cohere-embed-v4";

    public string RerankDeploymentName { get; set; } = "cohere-rerank-v4-pro";

    public string EmbedModelName { get; set; } = "embed-v-4-0";

    public string RerankModelName { get; set; } = "Cohere-rerank-v4.0-pro";

    public string EmbedEndpoint { get; set; } = string.Empty;

    public string RerankEndpoint { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public int EmbeddingDimensions { get; set; } = 1024;

    public int EmbeddingBatchSize { get; set; } = 16;

    public int MaxConcurrentEmbeddingRequests { get; set; } = 1;

    public int MaxConcurrentRerankRequests { get; set; } = 2;

    public bool RetryEnabled { get; set; } = true;

    public int MaxRetryAttempts { get; set; } = 4;

    public int BaseDelaySeconds { get; set; } = 1;

    public int MaxDelaySeconds { get; set; } = 30;
}
