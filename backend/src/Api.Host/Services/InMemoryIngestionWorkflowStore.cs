using System.Collections.Concurrent;
using System.Text;
using CohereRndKnowledgeMining.Api.Host.Contracts;
using CohereRndKnowledgeMining.Api.Host.Workflow;
using Microsoft.Agents.AI.Workflows;

namespace CohereRndKnowledgeMining.Api.Host.Services;

/// <summary>
/// In-memory state for a single block workflow run. Shared shape between Block 1 (Ingestion)
/// and Block 2 (Curate) executions.
/// </summary>
public sealed class WorkflowExecution
{
    public required string ExecutionId { get; init; }

    /// <summary>Source id (Block 1) or session id (Block 2).</summary>
    public required string CorrelationId { get; init; }

    public WorkflowStatus Status { get; set; } = WorkflowStatus.Pending;

    public string? CurrentAgent { get; set; }

    public Dictionary<string, AgentExecutionState> Agents { get; } = [];

    public Dictionary<string, string> AgentOutputs { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, StringBuilder> StreamingBuffers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public CheckpointManager? WorkflowCheckpointManager { get; set; }

    public CheckpointInfo? PendingCheckpoint { get; set; }

    public ExternalRequest? PendingApprovalRequest { get; set; }

    public HumanDecisionRecordResponse? HumanDecision { get; set; }

    public string? FailureReason { get; set; }

    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AgentExecutionState
{
    public required string AgentName { get; init; }

    public WorkflowStatus Status { get; set; }

    public string? Output { get; set; }
}

public sealed class InMemoryIngestionWorkflowStore
{
    private readonly ConcurrentDictionary<string, WorkflowExecution> _executions = new(StringComparer.OrdinalIgnoreCase);

    public void Save(WorkflowExecution execution) => _executions[execution.ExecutionId] = execution;

    public WorkflowExecution GetRequired(string executionId)
    {
        if (_executions.TryGetValue(executionId, out WorkflowExecution? execution))
        {
            return execution;
        }

        throw new KeyNotFoundException($"Ingestion workflow execution '{executionId}' was not found.");
    }

    public IReadOnlyList<WorkflowExecution> GetAll() =>
        _executions.Values.ToList();
}
