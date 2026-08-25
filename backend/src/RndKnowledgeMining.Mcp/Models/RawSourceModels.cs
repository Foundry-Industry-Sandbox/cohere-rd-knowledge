namespace RndKnowledgeMining.Mcp.Models;

/// <summary>
/// Metadata for a single raw R&amp;D document stored in Fabric OneLake. Excludes content
/// to keep list responses small; use <see cref="ReadRawDocumentResponse"/> to fetch content.
/// </summary>
public sealed class RawDocumentSummary
{
    public required string FileName { get; init; }

    /// <summary>article, protocol, eln_lims, dataset, result, submission, partner_repo, region_policy, ...</summary>
    public required string SourceType { get; init; }

    public required string SourcePath { get; init; }
}

public sealed class ListRawDocumentsResponse
{
    public required string SourceId { get; init; }

    public required IReadOnlyList<RawDocumentSummary> Documents { get; init; }
}

public sealed class ReadRawDocumentResponse
{
    public required string Title { get; init; }

    public required string SourceType { get; init; }

    public required string SourcePath { get; init; }

    public required string Content { get; init; }
}
