using Microsoft.Extensions.Options;
using RndKnowledgeMining.Governance;
using RndKnowledgeMining.Governance.Audit;
using RndKnowledgeMining.Governance.Mcp;

namespace RndKnowledgeMining.Mcp.Governance;

public static class McpGovernanceRegistration
{
    public static IServiceCollection AddMcpGovernance(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<GovernanceSettings>(configuration.GetSection(GovernanceSettings.SectionName));
        services.AddHttpContextAccessor();

        services.AddSingleton<IAgentGovernanceAuditStore>(sp =>
        {
            GovernanceSettings options = sp.GetRequiredService<IOptions<GovernanceSettings>>().Value;
            return new FileAgentGovernanceAuditStore(options.AgentAuditStoreDirectory);
        });

        services.AddSingleton<FoundryAgentGovernanceBootstrap>(_ => new FoundryAgentGovernanceBootstrap());

        services.AddSingleton<McpToolGovernanceCoordinator>();

        return services;
    }
}
