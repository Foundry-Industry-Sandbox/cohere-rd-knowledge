using System.ComponentModel;
using RndKnowledgeMining.Mcp.Adapters;
using RndKnowledgeMining.Mcp.Models;
using ModelContextProtocol.Server;

namespace RndKnowledgeMining.Mcp.Tools;

public sealed class KnowledgeSearchTools
{
    private readonly IKnowledgeSearchService _knowledgeSearchService;
    private readonly INormalizedDocumentStore _normalizedDocumentStore;

    public KnowledgeSearchTools(
        IKnowledgeSearchService knowledgeSearchService,
        INormalizedDocumentStore normalizedDocumentStore)
    {
        _knowledgeSearchService = knowledgeSearchService;
        _normalizedDocumentStore = normalizedDocumentStore;
    }

    [McpServerTool]
    [Description("Searches curated R&D knowledge in Azure AI Search using Cohere embed + rerank.")]
    public Task<SearchRdKnowledgeResponse> SearchRdKnowledge(
        string sessionId,
        [Description("Required natural-language query describing the R&D topic, study, compound, endpoint, protocol, dataset, or policy area.")]
        string query,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        return _knowledgeSearchService.SearchAsync(sessionId, query, topK, cancellationToken);
    }

    [McpServerTool]
    [Description("Resolves document-to-dataset-to-study lineage for a retrieved passage identifier.")]
    public Task<KnowledgeLineageResponse> GetKnowledgeLineage(
        string sessionId,
        [Description("Passage identifier returned by search_rd_knowledge (passageId field from a match), for example RDOC-PMC6889286-0.")]
        string passageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(passageId);
        return _knowledgeSearchService.GetLineageAsync(sessionId, passageId, cancellationToken);
    }

    [McpServerTool]
    [Description("Indexes a metadata-linking chunk into the R&D knowledge evidence index using Foundry embeddings.")]
    public Task<IndexRdKnowledgeResponse> IndexRdKnowledge(
        string sessionId,
        [Description("Canonical entity id associated with this chunk (for example doc-pmc6889286, TRIAL-MARIPOSA, DATASET-GSE323366).")]
        string entityId,
        [Description("Entity type for retrieval filters and lineage (for example document, trial, dataset, protocol, regulatory).")]
        string entityType,
        [Description("Human-readable title for the source document or entity.")]
        string title,
        [Description("Embeddable chunk text to persist in the Vector DB index.")]
        string chunkText,
        [Description("Optional passage identifier. When omitted, a deterministic value is generated from entityId and chunk content.")]
        string? passageId = null,
        [Description("Optional linked entity ids used by get_knowledge_lineage to resolve document-to-dataset-to-study traceability.")]
        IReadOnlyList<string>? linkedEntities = null,
        [Description("Optional lineage narrative to store alongside linked entities for traceability.")]
        string? lineageNarrative = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(chunkText);

        return _knowledgeSearchService.IndexAsync(
            sessionId,
            entityId,
            entityType,
            title,
            chunkText,
            linkedEntities,
            lineageNarrative,
            passageId,
            cancellationToken);
    }

    [McpServerTool]
    [Description("Indexes multiple metadata-linking chunks into the R&D knowledge evidence index in a single batch call (max 24 items per call).")]
    public Task<IndexRdKnowledgeBatchResponse> IndexRdKnowledgeBatch(
        string sessionId,
        [Description("List of chunks and metadata to index in one request.")]
        IReadOnlyList<IndexRdKnowledgeBatchItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new ArgumentException("At least one indexing item is required.", nameof(items));
        }

        return _knowledgeSearchService.IndexBatchAsync(sessionId, items, cancellationToken);
    }

    [McpServerTool]
    [Description("Lists normalized ingestion documents persisted for a Block 1 batch. Returns documentId metadata only; use read_normalized_document to fetch each document JSON.")]
    public Task<ListNormalizedDocumentsResponse> ListNormalizedDocuments(
        [Description("Ingestion source/batch id (e.g. case-01-human-review).")]
        string sourceId,
        [Description("Workflow execution id for the ingestion run (32-character hex string from the handoff payload executionId field — copy exactly).")]
        string executionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        return _normalizedDocumentStore.ListAsync(sourceId, executionId, cancellationToken);
    }

    [McpServerTool]
    [Description("Reads one normalized ingestion document JSON persisted after ingestion-translation completes. Returns a compact agent view (capped sections/references) suitable for linking without blowing the model context window.")]
    public Task<ReadNormalizedDocumentResponse> ReadNormalizedDocument(
        [Description("Ingestion source/batch id (e.g. case-01-human-review).")]
        string sourceId,
        [Description("Workflow execution id — use the executionId returned by list_normalized_documents (copy exactly).")]
        string executionId,
        [Description("Canonical documentId from list_normalized_documents (e.g. doc-pmc5447962).")]
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        return _normalizedDocumentStore.ReadAsync(sourceId, executionId, documentId, cancellationToken);
    }
}
