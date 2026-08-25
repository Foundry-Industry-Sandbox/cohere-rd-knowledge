using AgentGovernance;
using AgentGovernance.Policy;

namespace RndKnowledgeMining.Governance;

public sealed class FoundryAgentGovernanceBootstrap
{
    private readonly string? _policiesBaseDirectory;
    private readonly Dictionary<AgentRole, GovernanceKernel> _kernels = new();
    private readonly Dictionary<AgentRole, RoguePolicyConfig> _rogueConfigs = new();

    public FoundryAgentGovernanceBootstrap(string? policiesBaseDirectory = null)
    {
        _policiesBaseDirectory = policiesBaseDirectory;
    }

    public GovernanceKernel GetKernel(AgentRole role)
    {
        if (_kernels.TryGetValue(role, out GovernanceKernel? existing))
        {
            return existing;
        }

        FoundryAgentGovernancePolicyPaths.EnsurePolicyFilesExist(role, _policiesBaseDirectory);

        string governancePath = FoundryAgentGovernancePolicyPaths.ResolveGovernancePolicyPath(role, _policiesBaseDirectory);
        var kernel = new GovernanceKernel(new GovernanceOptions
        {
            PolicyPaths = [governancePath],
            ConflictStrategy = ConflictResolutionStrategy.DenyOverrides,
            EnableRings = false,
            EnablePromptInjectionDetection = false,
            EnableCircuitBreaker = true
        });

        _kernels[role] = kernel;
        _rogueConfigs[role] = RoguePolicyConfig.Load(role, _policiesBaseDirectory);
        return kernel;
    }

    public RoguePolicyConfig GetRogueConfig(AgentRole role)
    {
        if (_rogueConfigs.TryGetValue(role, out RoguePolicyConfig? existing))
        {
            return existing;
        }

        GetKernel(role);
        return _rogueConfigs[role];
    }
}
