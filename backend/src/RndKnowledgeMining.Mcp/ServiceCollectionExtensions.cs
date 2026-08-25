using System.Collections.Concurrent;
using System.Reflection;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using RndKnowledgeMining.Governance;
using RndKnowledgeMining.Governance.Mcp;using RndKnowledgeMining.Mcp.Adapters;
using RndKnowledgeMining.Mcp.Governance;
using RndKnowledgeMining.Mcp.Options;
using RndKnowledgeMining.Mcp.Startup;
using RndKnowledgeMining.Mcp.Tools;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace RndKnowledgeMining.Mcp;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAzureMonitorTelemetryIfConfigured(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string? connectionString = configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
            ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddOpenTelemetry().UseAzureMonitor();
        }

        return services;
    }

    /// <summary>
    /// Registers Azure AI Search + Foundry embed/rerank services shared by the MCP host and API host.
    /// </summary>
    public static IServiceCollection AddKnowledgeSearchServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AzureSearchOptions>(configuration.GetSection(AzureSearchOptions.SectionName));
        services.Configure<AzureFoundryModelsOptions>(configuration.GetSection(AzureFoundryModelsOptions.SectionName));

        var searchOptions = configuration.GetSection(AzureSearchOptions.SectionName).Get<AzureSearchOptions>()
            ?? new AzureSearchOptions();
        var foundryOptions = configuration.GetSection(AzureFoundryModelsOptions.SectionName).Get<AzureFoundryModelsOptions>()
            ?? new AzureFoundryModelsOptions();

        ValidateRequiredConfiguration(searchOptions, foundryOptions);

        services.AddSingleton(SearchClientFactory.CreateIndexClient(searchOptions));

        services.AddHttpClient<FoundryEmbeddingService>(client => client.Timeout = TimeSpan.FromMinutes(10))
            .AddFoundryResilience(foundryOptions);
        services.AddHttpClient<FoundryRerankService>(client => client.Timeout = TimeSpan.FromMinutes(10))
            .AddFoundryResilience(foundryOptions);

        services.AddSingleton<KnowledgeIndexAdapter>();
        services.AddSingleton<IKnowledgeSearchService, AzureKnowledgeSearchService>();

        return services;
    }

    public static IServiceCollection AddRndKnowledgeMiningMcpServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PolicySeedOptions>(configuration.GetSection(PolicySeedOptions.SectionName));
        services.Configure<McpStartupOptions>(configuration.GetSection(McpStartupOptions.SectionName));
        services.Configure<DataSourceOptions>(configuration.GetSection(DataSourceOptions.SectionName));
        services.Configure<DatasetOptions>(configuration.GetSection(DatasetOptions.SectionName));

        services.AddKnowledgeSearchServices(configuration);

        var dataSourceOptions = configuration.GetSection(DataSourceOptions.SectionName).Get<DataSourceOptions>()
            ?? new DataSourceOptions();
        var datasetOptions = configuration.GetSection(DatasetOptions.SectionName).Get<DatasetOptions>()
            ?? new DatasetOptions();

        var fabricLakehouseOptions = dataSourceOptions.FabricLakehouse ?? new FabricLakehouseOptions();

        if (dataSourceOptions.Mode == DataSourceMode.Fabric)
        {
            ValidateFabricLakehouseConfiguration(fabricLakehouseOptions);
            services.Configure<FabricLakehouseOptions>(
                configuration.GetSection($"{DataSourceOptions.SectionName}:{FabricLakehouseOptions.SectionName}"));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(datasetOptions.RootPath))
            {
                throw new InvalidOperationException(
                    "DataSource:Mode is Local but Dataset:RootPath is missing.");
            }
        }

        services.AddSingleton<PolicyParser>();
        services.AddSingleton<SensitiveContentScanner>();
        services.AddSingleton<SearchIndexInitializer>();
        services.AddSingleton<PolicyIndexAdapter>();
        services.AddSingleton<PolicyIndexSeeder>();
        services.AddSingleton<PolicySeedRunner>();
        services.AddSingleton<IPolicySearchService, AzurePolicySearchService>();
        services.AddSingleton<KnowledgeSearchTools>();
        services.AddSingleton<CurationComplianceTools>();
        services.AddNormalizedDocumentStore(configuration);

        if (dataSourceOptions.Mode == DataSourceMode.Fabric)
        {
            services.AddSingleton<FabricLakehouseClient>(_ =>
                FabricLakehouseClient.Create(
                    fabricLakehouseOptions,
                    _.GetRequiredService<ILogger<FabricLakehouseClient>>()));
            services.AddSingleton<IRawSourceService, FabricRawSourceService>();
        }
        else
        {
            services.AddSingleton<IRawSourceService>(sp =>
                new LocalRawSourceService(
                    datasetOptions.RootPath,
                    sp.GetRequiredService<ILogger<LocalRawSourceService>>()));
        }

        services.AddSingleton<RawSourceTools>();
        services.AddHostedService<McpStartupInitializer>();

        return services;
    }

    private static void ValidateRequiredConfiguration(
        AzureSearchOptions searchOptions,
        AzureFoundryModelsOptions foundryOptions)
    {
        if (string.IsNullOrWhiteSpace(searchOptions.Endpoint))
        {
            throw new InvalidOperationException("AzureSearch:Endpoint is required.");
        }

        if (string.IsNullOrWhiteSpace(foundryOptions.EmbedEndpoint))
        {
            throw new InvalidOperationException("AzureFoundryModels:EmbedEndpoint is required.");
        }

        if (string.IsNullOrWhiteSpace(foundryOptions.RerankEndpoint))
        {
            throw new InvalidOperationException("AzureFoundryModels:RerankEndpoint is required.");
        }
    }

    private static void ValidateFabricLakehouseConfiguration(FabricLakehouseOptions fabricLakehouseOptions)
    {
        if (string.IsNullOrWhiteSpace(fabricLakehouseOptions.WorkspaceName))
        {
            throw new InvalidOperationException("FabricLakehouse:WorkspaceName is required.");
        }

        if (string.IsNullOrWhiteSpace(fabricLakehouseOptions.LakehouseName))
        {
            throw new InvalidOperationException("FabricLakehouse:LakehouseName is required.");
        }
    }

    public static void PopulateToolDictionary(
        IServiceProvider serviceProvider,
        ConcurrentDictionary<string, McpServerTool[]> toolDictionary)
    {
        GovernanceSettings settings = serviceProvider
            .GetRequiredService<IOptions<GovernanceSettings>>()
            .Value;
        McpToolGovernanceCoordinator? coordinator = settings.EnableMcpToolGovernance
            ? serviceProvider.GetRequiredService<McpToolGovernanceCoordinator>()
            : null;
        IHttpContextAccessor? httpContextAccessor = settings.EnableMcpToolGovernance
            ? serviceProvider.GetRequiredService<IHttpContextAccessor>()
            : null;

        toolDictionary["knowledge-search"] = CreateTools(
            serviceProvider.GetRequiredService<KnowledgeSearchTools>(),
            coordinator,
            httpContextAccessor);
        toolDictionary["curation-compliance"] = CreateTools(
            serviceProvider.GetRequiredService<CurationComplianceTools>(),
            coordinator,
            httpContextAccessor);
        toolDictionary["raw-source"] = CreateTools(
            serviceProvider.GetRequiredService<RawSourceTools>(),
            coordinator,
            httpContextAccessor);
    }

    private static McpServerTool[] CreateTools<T>(
        T target,
        McpToolGovernanceCoordinator? coordinator,
        IHttpContextAccessor? httpContextAccessor)
    {
        var tools = new List<McpServerTool>();
        var methods = typeof(T).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null);

        foreach (var method in methods)
        {
            McpServerTool innerTool = McpServerTool.Create(method, target);
            tools.Add(coordinator is not null && httpContextAccessor is not null
                ? new GovernedMcpServerTool(innerTool, coordinator, httpContextAccessor)
                : innerTool);
        }

        return tools.ToArray();
    }
}
