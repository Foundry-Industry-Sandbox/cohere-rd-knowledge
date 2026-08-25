using CohereRndKnowledge.AgentProvisioning.Models;

namespace CohereRndKnowledge.AgentProvisioning;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            string? configPath = null;
            string? agentsRoot = null;

            for (int index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--config" when index + 1 < args.Length:
                        configPath = args[++index];
                        break;
                    case "--agents" when index + 1 < args.Length:
                        agentsRoot = args[++index];
                        break;
                    case "--help":
                    case "-h":
                        PrintUsage();
                        return 0;
                    default:
                        Console.Error.WriteLine($"Unknown argument: {args[index]}");
                        PrintUsage();
                        return 1;
                }
            }

            ProvisioningSettings settings = SettingsLoader.Load();
            AgentAssetLoader assetLoader = new(AgentAssetLoader.ResolveAgentsRoot(agentsRoot));
            IReadOnlyList<AgentAssetBundle> bundles = assetLoader.LoadAll();

            Console.WriteLine($"Provisioning {bundles.Count} agents to {settings.ProjectEndpoint}");
            Console.WriteLine($"Default model deployment: {settings.ModelDeploymentName}");
            Console.WriteLine($"Metadata model deployment: {settings.MetadataModelDeploymentName}");
            Console.WriteLine($"MCP base URL: {settings.McpBaseUrl}");

            FoundryAgentProvisioner provisioner = new();
            IReadOnlyList<AgentProvisionResult> results =
                await provisioner.ProvisionAllAsync(settings, bundles, CancellationToken.None).ConfigureAwait(false);

            foreach (AgentProvisionResult result in results)
            {
                Console.WriteLine($"{result.Outcome,-10} {result.AgentName} - {result.Message}");
            }

            Console.WriteLine();
            Console.WriteLine("Governance report:");
            Console.WriteLine($"  governanceToolkitVersion: {GovernanceProvisioningMetadata.GovernanceToolkitVersion}");
            Console.WriteLine($"  policyBundleVersion: {GovernanceProvisioningMetadata.PolicyBundleVersion}");
            Console.WriteLine($"  governedAgents: [{string.Join(", ", bundles.Select(bundle => bundle.Manifest.Name))}]");

            Console.WriteLine("Agent provisioning completed successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Agent provisioning failed: {ex.Message}");
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Cohere R&D Knowledge Mining Agent Provisioning");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --no-restore --project agent-provisioning/src/CohereRndKnowledge.AgentProvisioning");
        Console.WriteLine("    [--config path/to/provisioning.json]");
        Console.WriteLine("    [--agents path/to/agents]");
        Console.WriteLine();
        Console.WriteLine("Environment variables:");
        Console.WriteLine("  AZURE_FOUNDRY_PROJECT_ENDPOINT or FOUNDRY_PROJECT_ENDPOINT");
        Console.WriteLine("  AZURE_AI_MODEL_DEPLOYMENT_NAME or ModelDeploymentName");
        Console.WriteLine("  AZURE_AI_METADATA_MODEL_DEPLOYMENT_NAME (optional; defaults to the primary model)");
        Console.WriteLine("  MCP_BASE_URL");
    }
}
