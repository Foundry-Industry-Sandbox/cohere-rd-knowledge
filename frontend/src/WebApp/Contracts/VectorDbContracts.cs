namespace Cohere.AgenticRDKnowledge.Shared.Contracts.VectorDb;

public sealed record VectorDbStoreSummary(
    int TotalStudies,
    int TotalDocuments,
    int TotalEntities,
    int TotalLinks,
    DateTimeOffset? LastIngestionAt,
    string? LastIngestedStudyId);

public sealed record VectorDbClearDataResult(long DeletedCount);
