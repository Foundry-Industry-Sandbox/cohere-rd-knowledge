using Cohere.AgenticRDKnowledge.Shared.Contracts.VectorDb;
using Cohere.AgenticRDKnowledge.WebApp.Models;
using Cohere.AgenticRDKnowledge.WebApp.Services;

namespace Cohere.AgenticRDKnowledge.WebApp.State;

public sealed class KnowledgePortfolioState
{
    private readonly IRdKnowledgeApiClient _apiClient;
    private readonly KnowledgeSessionStore _sessionStore;
    private readonly PortfolioScenarioService _scenarios;

    public bool IsLoading { get; private set; }
    public string? Error { get; private set; }
    public VectorDbStoreSummary? VectorDbSummary { get; private set; }
    public IReadOnlyList<SeedScenarioDefinition> IngestionScenarios { get; private set; } = [];
    public IReadOnlyList<SeedScenarioDefinition> QueryScenarios { get; private set; } = [];
    public IReadOnlyList<KnowledgeSessionSummary> ActiveSessions { get; private set; } = [];

    public event Action? OnChange;

    public KnowledgePortfolioState(
        IRdKnowledgeApiClient apiClient,
        KnowledgeSessionStore sessionStore,
        PortfolioScenarioService scenarios)
    {
        _apiClient = apiClient;
        _sessionStore = sessionStore;
        _scenarios = scenarios;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        Error = null;
        Notify();

        try
        {
            await _scenarios.EnsureLoadedAsync(cancellationToken);
            IngestionScenarios = _scenarios.GetIngestionScenarios();
            QueryScenarios = _scenarios.GetQueryScenarios();
            VectorDbSummary = await _apiClient.GetVectorDbStoreSummaryAsync(cancellationToken);
        }
        catch
        {
            VectorDbSummary = null;
        }

        try
        {
            RefreshSessions();
        }
        catch
        {
            // Session store is in-memory; safe to ignore.
        }
        finally
        {
            IsLoading = false;
            Notify();
        }
    }

    public void RefreshSessions()
    {
        ActiveSessions = _sessionStore.GetSummaries();
        Notify();
    }

    private void Notify() => OnChange?.Invoke();
}
