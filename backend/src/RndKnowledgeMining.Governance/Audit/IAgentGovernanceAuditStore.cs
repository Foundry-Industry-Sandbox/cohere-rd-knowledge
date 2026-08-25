namespace RndKnowledgeMining.Governance.Audit;

public interface IAgentGovernanceAuditStore
{
    void Append(AgentGovernanceAuditRecord record);
}
