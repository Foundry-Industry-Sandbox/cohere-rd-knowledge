using RndKnowledgeMining.Mcp.Adapters;
using RndKnowledgeMining.Mcp.Models;
using RndKnowledgeMining.Mcp.Options;
using Microsoft.Extensions.Options;

namespace RndKnowledgeMining.Mcp.Startup;

public sealed class PolicyIndexSeeder
{
    private readonly PolicyParser _policyParser;
    private readonly PolicyIndexAdapter _policyIndexAdapter;
    private readonly PolicySeedOptions _policySeedOptions;
    private readonly ILogger<PolicyIndexSeeder> _logger;

    public PolicyIndexSeeder(
        PolicyParser policyParser,
        PolicyIndexAdapter policyIndexAdapter,
        IOptions<PolicySeedOptions> policySeedOptions,
        IHostEnvironment environment,
        ILogger<PolicyIndexSeeder> logger)
    {
        _policyParser = policyParser;
        _policyIndexAdapter = policyIndexAdapter;
        _policySeedOptions = policySeedOptions.Value;
        _policySeedOptions.PolicyFilePath = ResolveContentPath(
            environment.ContentRootPath,
            _policySeedOptions.PolicyFilePath);
        _logger = logger;
    }

    public async Task SeedIfNeededAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_policySeedOptions.PolicyFilePath))
        {
            throw new FileNotFoundException(
                $"Policy file was not found at '{_policySeedOptions.PolicyFilePath}'.");
        }

        string policyText = await File.ReadAllTextAsync(_policySeedOptions.PolicyFilePath, cancellationToken);
        string contentHash = PolicyParser.ComputeContentHash(policyText);
        string? storedHash = await _policyIndexAdapter.GetStoredContentHashAsync(cancellationToken);

        if (string.Equals(storedHash, contentHash, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Policy index is up to date. Skipping reindex.");
            return;
        }

        IReadOnlyList<PolicyEntry> policies = _policyParser.Parse(policyText);
        await _policyIndexAdapter.SeedPoliciesAsync(policies, contentHash, cancellationToken);
        _logger.LogInformation("Seeded {PolicyCount} policies into the policy index.", policies.Count);
    }

    private static string ResolveContentPath(string contentRootPath, string path) =>
        string.IsNullOrWhiteSpace(path)
            ? path
            : Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(contentRootPath, path));
}

public sealed class PolicySeedRunner
{
    private readonly PolicyIndexSeeder _policyIndexSeeder;
    private readonly SearchIndexInitializer _searchIndexInitializer;
    private readonly ILogger<PolicySeedRunner> _logger;

    public PolicySeedRunner(
        PolicyIndexSeeder policyIndexSeeder,
        SearchIndexInitializer searchIndexInitializer,
        ILogger<PolicySeedRunner> logger)
    {
        _policyIndexSeeder = policyIndexSeeder;
        _searchIndexInitializer = searchIndexInitializer;
        _logger = logger;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        await _searchIndexInitializer.EnsureIndexesAsync(cancellationToken);
        await _policyIndexSeeder.SeedIfNeededAsync(cancellationToken);
        _logger.LogInformation("Policy seed completed.");
        return 0;
    }
}

public sealed class McpStartupInitializer : IHostedService
{
    private readonly SearchIndexInitializer _searchIndexInitializer;
    private readonly PolicyIndexSeeder _policyIndexSeeder;
    private readonly McpStartupOptions _startupOptions;
    private readonly ILogger<McpStartupInitializer> _logger;

    public McpStartupInitializer(
        SearchIndexInitializer searchIndexInitializer,
        PolicyIndexSeeder policyIndexSeeder,
        IOptions<McpStartupOptions> startupOptions,
        ILogger<McpStartupInitializer> logger)
    {
        _searchIndexInitializer = searchIndexInitializer;
        _policyIndexSeeder = policyIndexSeeder;
        _startupOptions = startupOptions.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_startupOptions.EnsureSearchIndexesOnStartup)
        {
            _logger.LogInformation("Ensuring Azure AI Search indexes exist.");
            await _searchIndexInitializer.EnsureIndexesAsync(cancellationToken);
        }

        if (_startupOptions.SeedPoliciesOnStartup)
        {
            _logger.LogInformation("Seeding policy index if needed.");
            await _policyIndexSeeder.SeedIfNeededAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
