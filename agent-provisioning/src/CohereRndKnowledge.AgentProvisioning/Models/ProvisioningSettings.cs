namespace CohereRndKnowledge.AgentProvisioning.Models;

public sealed class ProvisioningSettings
{
    public string ProjectEndpoint { get; set; } = string.Empty;

    public string ModelDeploymentName { get; set; } = string.Empty;

    public string MetadataModelDeploymentName { get; set; } = string.Empty;

    public string McpBaseUrl { get; set; } = string.Empty;
}
