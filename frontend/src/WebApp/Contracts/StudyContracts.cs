namespace Cohere.AgenticRDKnowledge.Shared.Contracts.Studies;

public sealed record StudySummary(
    string StudyId,
    string Title,
    string Compound,
    string Phase,
    string PrimaryEndpoint,
    IReadOnlyList<string> SourceSystems);

public sealed record KnowledgeSource(
    string SourceId,
    string Title,
    string SourceType,
    string Format,
    string Summary);

public sealed record StudyDocumentsResponse(
    string StudyId,
    IReadOnlyList<KnowledgeSource> Sources);
