using Cohere.AgenticRDKnowledge.Shared.Contracts;
using Cohere.AgenticRDKnowledge.WebApp.Models;

namespace Cohere.AgenticRDKnowledge.WebApp.Services;

public sealed class PortfolioScenarioService
{
    private readonly IRdKnowledgeApiClient _apiClient;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private IReadOnlyList<SeedScenarioDefinition> _scenarios = [];
    private bool _isLoaded;

    public PortfolioScenarioService(IRdKnowledgeApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_isLoaded)
        {
            return;
        }

        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isLoaded)
            {
                return;
            }

            _scenarios = await _apiClient.GetPortfolioScenariosAsync(cancellationToken).ConfigureAwait(false);
            _isLoaded = true;
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public IReadOnlyList<SeedScenarioDefinition> GetScenarios() => _scenarios;

    public IReadOnlyList<SeedScenarioDefinition> GetIngestionScenarios() =>
        _scenarios.Where(s => s.Block.Equals("Ingestion", StringComparison.OrdinalIgnoreCase)).ToList();

    public IReadOnlyList<SeedScenarioDefinition> GetQueryScenarios() =>
        _scenarios.Where(s => s.Block.Equals("Query", StringComparison.OrdinalIgnoreCase)).ToList();

    public SeedScenarioDefinition? GetScenario(string scenarioId) =>
        _scenarios.FirstOrDefault(s => s.ScenarioId.Equals(scenarioId, StringComparison.OrdinalIgnoreCase));

    public SeedScenarioDefinition? GetScenarioByStudyId(string studyId, WorkflowBlock block) =>
        _scenarios.FirstOrDefault(s =>
            s.StudyId.Equals(studyId, StringComparison.OrdinalIgnoreCase) &&
            s.Block.Equals(block.ToString(), StringComparison.OrdinalIgnoreCase));

    public SeedScenarioDefinition? GetScenarioBySourceId(string sourceId, WorkflowBlock block) =>
        _scenarios.FirstOrDefault(s =>
            s.SourceId.Equals(sourceId, StringComparison.OrdinalIgnoreCase) &&
            s.Block.Equals(block.ToString(), StringComparison.OrdinalIgnoreCase));

    public SeedScenarioDefinition? ResolveIngestionScenario(string studyOrSourceId) =>
        GetScenarioByStudyId(studyOrSourceId, WorkflowBlock.Ingestion)
        ?? GetScenarioBySourceId(studyOrSourceId, WorkflowBlock.Ingestion);

    public string? GetSourceIdByStudyId(string studyId, WorkflowBlock block) =>
        GetScenarioByStudyId(studyId, block)?.SourceId is { Length: > 0 } sourceId
            ? sourceId
            : null;
}
