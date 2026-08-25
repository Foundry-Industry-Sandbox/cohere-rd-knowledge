using System.Text.Json;
using CohereRndKnowledgeMining.Api.Host.Contracts;
using CohereRndKnowledgeMining.Api.Host.Options;
using Microsoft.Extensions.Options;

namespace CohereRndKnowledgeMining.Api.Host.Services;

public sealed class PortfolioScenarioCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly DatasetOptions _datasetOptions;
    private readonly ILogger<PortfolioScenarioCatalogService> _logger;

    public PortfolioScenarioCatalogService(
        IOptions<DatasetOptions> datasetOptions,
        ILogger<PortfolioScenarioCatalogService> logger)
    {
        _datasetOptions = datasetOptions.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PortfolioScenarioResponse>> LoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_datasetOptions.RootPath))
        {
            throw new InvalidOperationException("Dataset:RootPath is required to load portfolio scenarios.");
        }

        string catalogPath = Path.Combine(_datasetOptions.RootPath, "cases", "catalog.json");
        if (!File.Exists(catalogPath))
        {
            throw new FileNotFoundException($"Scenario catalog not found: {catalogPath}", catalogPath);
        }

        await using FileStream stream = File.OpenRead(catalogPath);
        List<PortfolioScenarioResponse>? scenarios =
            await JsonSerializer.DeserializeAsync<List<PortfolioScenarioResponse>>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

        _logger.LogInformation(
            "Loaded {Count} portfolio scenarios from {CatalogPath}.",
            scenarios?.Count ?? 0,
            catalogPath);

        return scenarios ?? [];
    }
}
