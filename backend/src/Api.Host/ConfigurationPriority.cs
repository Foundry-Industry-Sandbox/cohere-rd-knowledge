using Microsoft.Extensions.Configuration;

namespace CohereRndKnowledgeMining.Api.Host;

/// <summary>
/// Ensures environment variables and command-line args override any JSON appsettings layers
/// (including optional local overlays added after host defaults).
/// </summary>
internal static class ConfigurationPriority
{
    public static void PreferEnvironmentVariables(ConfigurationManager configuration, string[] args)
    {
        configuration.AddEnvironmentVariables();
        configuration.AddCommandLine(args);
    }
}
