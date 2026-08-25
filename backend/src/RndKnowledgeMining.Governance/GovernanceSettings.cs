namespace RndKnowledgeMining.Governance;

public sealed class GovernanceSettings
{
    public const string SectionName = "Governance";

    public bool EnableMcpToolGovernance { get; set; } = true;

    public string AgentAuditStoreDirectory { get; set; } = "data/agent-governance-audit";
}
