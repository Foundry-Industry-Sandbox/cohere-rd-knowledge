using System.Text.Json;
using AgentGovernance.Audit;

namespace RndKnowledgeMining.Governance.Audit;

public sealed class FileAgentGovernanceAuditStore : IAgentGovernanceAuditStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly AuditLogger _auditLogger = new();
    private readonly string _auditFilePath;
    private readonly object _sync = new();

    public FileAgentGovernanceAuditStore(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException("Audit directory path is required.", nameof(directoryPath));
        }

        Directory.CreateDirectory(directoryPath);
        _auditFilePath = Path.Combine(directoryPath, "agent-governance-audit.jsonl");
    }

    public void Append(AgentGovernanceAuditRecord record)
    {
        lock (_sync)
        {
            AuditEntry entry = _auditLogger.Log(record.AgentId, record.Action, record.Decision);
            AgentGovernanceAuditRecord persisted = record with
            {
                Seq = entry.Seq,
                TimestampUtc = entry.Timestamp,
                PreviousHash = entry.PreviousHash,
                Hash = entry.Hash
            };

            string line = JsonSerializer.Serialize(persisted, JsonOptions);
            File.AppendAllText(_auditFilePath, line + Environment.NewLine);
        }
    }
}
