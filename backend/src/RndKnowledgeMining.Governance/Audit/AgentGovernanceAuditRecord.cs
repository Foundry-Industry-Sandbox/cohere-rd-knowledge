namespace RndKnowledgeMining.Governance.Audit;

public sealed record AgentGovernanceAuditRecord(
    long Seq,
    DateTimeOffset TimestampUtc,
    string AgentId,
    string Action,
    string Decision,
    string PreviousHash,
    string Hash,
    string? CaseId,
    string? ExecutionId,
    string? EventType);
