using System.Collections.Concurrent;

namespace CohereRndKnowledgeMining.Api.Host.Services;

/// <summary>
/// Retains preloaded raw source documents for an ingestion run so the bridge can
/// rebuild full normalized document payloads from a manifest-only agent handoff.
/// </summary>
public sealed class IngestionSourceDocumentCache
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<RawKnowledgeItem>> _itemsByExecution =
        new(StringComparer.OrdinalIgnoreCase);

    public void Save(string executionId, IReadOnlyList<RawKnowledgeItem> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ArgumentNullException.ThrowIfNull(items);
        _itemsByExecution[executionId] = items;
    }

    public bool TryGet(string executionId, out IReadOnlyList<RawKnowledgeItem> items) =>
        _itemsByExecution.TryGetValue(executionId, out items!);

    public void Remove(string executionId) =>
        _itemsByExecution.TryRemove(executionId, out _);
}
