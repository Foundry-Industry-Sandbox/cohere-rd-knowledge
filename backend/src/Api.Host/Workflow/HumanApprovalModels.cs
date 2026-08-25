namespace CohereRndKnowledgeMining.Api.Host.Workflow;

/// <summary>
/// Prompt emitted to the human reviewer at a workflow approval gate.
/// Shared by both blocks: Block 1 uses it for the Knowledge Curator gate and
/// Block 2 uses it for the Compliance Reviewer gate.
/// </summary>
public sealed class HumanApprovalPrompt
{
    /// <summary>Source id (Block 1 ingestion) or session id (Block 2 curate).</summary>
    public required string CorrelationId { get; init; }

    public required string ExecutionId { get; init; }

    /// <summary>Reviewer persona expected to act on the gate (e.g. "knowledge-curator", "compliance-reviewer").</summary>
    public required string ReviewerRole { get; init; }

    public required string Summary { get; init; }

    /// <summary>Raw agent output being reviewed.</summary>
    public required string ReviewedOutput { get; init; }
}

public sealed class HumanApprovalDecision
{
    public required bool Approved { get; init; }

    public string? ReviewerComment { get; init; }
}
