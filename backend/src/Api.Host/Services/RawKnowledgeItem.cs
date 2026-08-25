namespace CohereRndKnowledgeMining.Api.Host.Services;

/// <summary>
/// A single raw R&D knowledge artifact read from Microsoft Fabric (Block 1 input).
/// </summary>
public sealed class RawKnowledgeItem
{
    public required string ItemId { get; init; }

    public required string Title { get; init; }

    /// <summary>article, protocol, eln_lims, dataset, result, submission, partner_repo, region_policy, ...</summary>
    public required string SourceType { get; init; }

    public required string Content { get; init; }

    public string? SourcePath { get; init; }
}

/// <summary>
/// A passage retrieved from the Vector DB for grounding a Search &amp; Chat answer (Block 2).
/// </summary>
public sealed class RetrievedPassage
{
    public required string PassageId { get; init; }

    public required string Content { get; init; }

    public required string Citation { get; init; }

    public double Score { get; init; }
}
