using CohereRndKnowledgeMining.Api.Host.Options;
using CohereRndKnowledgeMining.Api.Host.Services;
using CohereRndKnowledgeMining.Api.Host.Services.Integrations;
using CohereRndKnowledgeMining.Api.Host.Workflow;
using Microsoft.Extensions.Options;
using RndKnowledgeMining.Mcp;
using RndKnowledgeMining.Mcp.Adapters;
using System.Text.Json.Serialization;
using ApiFabricLakehouseClient = CohereRndKnowledgeMining.Api.Host.Services.Integrations.FabricLakehouseClient;

var builder = WebApplication.CreateBuilder(args);
CohereRndKnowledgeMining.Api.Host.ConfigurationPriority.PreferEnvironmentVariables(builder.Configuration, args);

builder.Services.AddAzureMonitorTelemetryIfConfigured(builder.Configuration);
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.Configure<AzureFoundryOptions>(options =>
{
    builder.Configuration.GetSection(AzureFoundryOptions.SectionName).Bind(options);

    string? endpoint = builder.Configuration["AZURE_FOUNDRY_PROJECT_ENDPOINT"]
        ?? Environment.GetEnvironmentVariable("AZURE_FOUNDRY_PROJECT_ENDPOINT");

    if (!string.IsNullOrWhiteSpace(endpoint))
    {
        options.ProjectEndpoint = endpoint;
    }
});

// Data source: Local or Fabric.
builder.Services.Configure<DataSourceOptions>(
    builder.Configuration.GetSection(DataSourceOptions.SectionName));

var dsOptions = builder.Configuration.GetSection(DataSourceOptions.SectionName).Get<DataSourceOptions>()
    ?? new DataSourceOptions();

if (dsOptions.Mode == DataSourceMode.Fabric)
{
    builder.Services.Configure<FabricLakehouseOptions>(
        builder.Configuration.GetSection($"{DataSourceOptions.SectionName}:{FabricLakehouseOptions.SectionName}"));

    var fabOpts = builder.Configuration
        .GetSection($"{DataSourceOptions.SectionName}:{FabricLakehouseOptions.SectionName}")
        .Get<FabricLakehouseOptions>()
        ?? new FabricLakehouseOptions();
    if (string.IsNullOrWhiteSpace(fabOpts.WorkspaceName) || string.IsNullOrWhiteSpace(fabOpts.LakehouseName))
    {
        throw new InvalidOperationException(
            "DataSource:Mode is Fabric but FabricLakehouse:WorkspaceName and/or FabricLakehouse:LakehouseName are missing.");
    }

    builder.Services.AddSingleton(sp =>
        ApiFabricLakehouseClient.Create(
            sp.GetRequiredService<IOptions<FabricLakehouseOptions>>().Value,
            sp.GetRequiredService<ILogger<ApiFabricLakehouseClient>>()));
    builder.Services.AddSingleton<IFabricRawSourceReader, FabricRawSourceReader>();
    builder.Services.Configure<DatasetOptions>(builder.Configuration.GetSection(DatasetOptions.SectionName));
}
else
{
    var dsOpts = builder.Configuration.GetSection(DatasetOptions.SectionName).Get<DatasetOptions>()
        ?? new DatasetOptions();
    if (string.IsNullOrWhiteSpace(dsOpts.RootPath))
    {
        throw new InvalidOperationException(
            "DataSource:Mode is Local but Dataset:RootPath is missing.");
    }

    builder.Services.Configure<DatasetOptions>(builder.Configuration.GetSection(DatasetOptions.SectionName));
}

builder.Services.AddNormalizedDocumentStore(builder.Configuration);
builder.Services.AddSingleton<IngestionSourceDocumentCache>();
builder.Services.AddSingleton<IngestionSourceDocumentLoader>();
builder.Services.AddSingleton<PortfolioScenarioCatalogService>();

// Foundry agents shared by both blocks.
builder.Services.AddSingleton<FoundryAgentProvider>();

// Vector DB retrieval (shared KnowledgeIndexAdapter with MCP search_rd_knowledge).
builder.Services.AddKnowledgeSearchServices(builder.Configuration);
builder.Services.AddSingleton<IVectorKnowledgeRetriever, AzureVectorKnowledgeRetriever>();

// Block 1 - Ingestion.
builder.Services.AddSingleton<IngestionWorkflowFactory>();
builder.Services.AddSingleton<InMemoryIngestionWorkflowStore>();
builder.Services.AddSingleton<IngestionWorkflowService>();

// Block 2 - Query (Search & Chat + Curate).
builder.Services.AddSingleton<QueryWorkflowFactory>();
builder.Services.AddSingleton<InMemoryQuerySessionStore>();
builder.Services.AddSingleton<QueryWorkflowService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapControllers();

app.Run();
