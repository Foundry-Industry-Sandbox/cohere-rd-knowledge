namespace RndKnowledgeMining.Mcp.Models;

public sealed class NormalizedDocumentSummary
{
    public required string DocumentId { get; init; }

    public string? SourceFile { get; init; }

    public string? CanonicalType { get; init; }

    public required string StoragePath { get; init; }
}

public sealed class ListNormalizedDocumentsResponse
{
    public required string SourceId { get; init; }

    public required string ExecutionId { get; init; }

    public required string NormalizedRoot { get; init; }

    public required int DocumentsReceived { get; init; }

    public required IReadOnlyList<NormalizedDocumentSummary> Documents { get; init; }
}

public sealed class ReadNormalizedDocumentResponse
{
    public required string SourceId { get; init; }

    public required string ExecutionId { get; init; }

    public required string DocumentId { get; init; }

    public required string StoragePath { get; init; }

    public required string Content { get; init; }
}
