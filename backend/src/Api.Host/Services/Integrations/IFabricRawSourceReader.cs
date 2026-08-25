namespace CohereRndKnowledgeMining.Api.Host.Services.Integrations;

/// <summary>
/// Reads raw R&D knowledge from Microsoft Fabric (the upstream source for Block 1).
/// </summary>
public interface IFabricRawSourceReader
{
    Task<IReadOnlyList<RawKnowledgeItem>> ReadAsync(string sourceId, CancellationToken cancellationToken);
}
