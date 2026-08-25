namespace RndKnowledgeMining.Mcp.Models;

public sealed class KnowledgePassageMatch
{
    public required string PassageId { get; init; }

    public required string EntityId { get; init; }

    public required string EntityType { get; init; }

    public required string Title { get; init; }

    public required string Snippet { get; init; }

    public double Score { get; init; }
}

public sealed class SearchRdKnowledgeResponse
{
    public required string SessionId { get; init; }

    public required string Query { get; init; }

    public required IReadOnlyList<KnowledgePassageMatch> Matches { get; init; }
}

public sealed class KnowledgeLineageResponse
{
    public required string SessionId { get; init; }

    public required string PassageId { get; init; }

    public required string EntityId { get; init; }

    public required IReadOnlyList<string> LinkedEntities { get; init; }

    public required string Lineage { get; init; }
}

public sealed class IndexRdKnowledgeResponse
{
    public required string SessionId { get; init; }

    public required string EntityId { get; init; }

    public required string PassageId { get; init; }

    public bool Indexed { get; init; }
}

public sealed class IndexRdKnowledgeBatchItem
{
    public required string EntityId { get; init; }

    public required string EntityType { get; init; }

    public required string Title { get; init; }

    public required string ChunkText { get; init; }

    public string? PassageId { get; init; }

    public IReadOnlyList<string>? LinkedEntities { get; init; }

    public string? LineageNarrative { get; init; }
}

public sealed class IndexRdKnowledgeBatchResponse
{
    public required string SessionId { get; init; }

    public required int Requested { get; init; }

    public required int Indexed { get; init; }

    public required IReadOnlyList<IndexRdKnowledgeResponse> Results { get; init; }
}

public sealed class PolicyEntry
{
    public required string PolicyRef { get; init; }

    public required string Rule { get; init; }

    public required string Threshold { get; init; }

    public required string Action { get; init; }

    public required string Exception { get; init; }

    public required string FullText { get; init; }
}

public sealed class PolicyMatch
{
    public required string PolicyRef { get; init; }

    public required string Rule { get; init; }

    public required string Threshold { get; init; }

    public required string Action { get; init; }

    public required string Exception { get; init; }

    public double Score { get; init; }
}

public sealed class GetRelevantPoliciesResponse
{
    public required string Query { get; init; }

    public required IReadOnlyList<PolicyMatch> Policies { get; init; }
}

public sealed record KnowledgeIndexSummaryResult(
    long TotalDocuments,
    long TotalStudies,
    long TotalEntities,
    long TotalLinks,
    DateTimeOffset? LastIngestionAt,
    string? LastIngestedStudyId);

public sealed class FlagSensitiveContentResponse
{
    public required string SessionId { get; init; }

    public bool SensitiveContentFound { get; init; }

    public required IReadOnlyList<string> Flags { get; init; }

    public required IReadOnlyList<string> MatchedPatterns { get; init; }
}
