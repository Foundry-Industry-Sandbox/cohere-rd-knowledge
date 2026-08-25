using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RndKnowledgeMining.Governance;

public sealed class RoguePolicyConfig
{
    public required string RiskyTool { get; init; }

    public int WindowSize { get; init; } = 10;

    public int TriggerCount { get; init; } = 5;

    public static RoguePolicyConfig Load(AgentRole role, string? baseDirectory = null)
    {
        string path = FoundryAgentGovernancePolicyPaths.ResolveRoguePolicyPath(role, baseDirectory);
        string yaml = File.ReadAllText(path);

        IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        RoguePolicyConfig? config = deserializer.Deserialize<RoguePolicyConfig>(yaml);
        if (config is null || string.IsNullOrWhiteSpace(config.RiskyTool))
        {
            throw new InvalidOperationException($"Rogue policy at '{path}' is missing riskyTool.");
        }

        if (config.WindowSize <= 0)
        {
            throw new InvalidOperationException($"Rogue policy at '{path}' has invalid windowSize.");
        }

        if (config.TriggerCount <= 0)
        {
            throw new InvalidOperationException($"Rogue policy at '{path}' has invalid triggerCount.");
        }

        return config;
    }
}
