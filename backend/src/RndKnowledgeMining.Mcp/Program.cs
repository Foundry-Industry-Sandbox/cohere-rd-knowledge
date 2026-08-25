using System.Collections.Concurrent;
using RndKnowledgeMining.Mcp;
using RndKnowledgeMining.Mcp.Governance;
using RndKnowledgeMining.Mcp.Startup;
using ModelContextProtocol.Server;

if (args.Contains("--seed-policies", StringComparer.OrdinalIgnoreCase))
{
    var seedBuilder = WebApplication.CreateBuilder(args);
    seedBuilder.Services.AddAzureMonitorTelemetryIfConfigured(seedBuilder.Configuration);
    seedBuilder.Configuration.AddJsonFile("appsettings.Deployment.local.json", optional: true, reloadOnChange: true);
    ConfigurationPriority.PreferEnvironmentVariables(seedBuilder.Configuration, args);
    seedBuilder.Services.AddRndKnowledgeMiningMcpServices(seedBuilder.Configuration);

    var seedApp = seedBuilder.Build();
    var exitCode = await seedApp.Services
        .GetRequiredService<PolicySeedRunner>()
        .RunAsync(CancellationToken.None);

    await seedApp.DisposeAsync();
    Environment.ExitCode = exitCode;
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAzureMonitorTelemetryIfConfigured(builder.Configuration);
builder.Configuration.AddJsonFile("appsettings.Deployment.local.json", optional: true, reloadOnChange: true);
ConfigurationPriority.PreferEnvironmentVariables(builder.Configuration, args);

builder.Services.AddRndKnowledgeMiningMcpServices(builder.Configuration);
builder.Services.AddMcpGovernance(builder.Configuration);

var toolDictionary = new ConcurrentDictionary<string, McpServerTool[]>(StringComparer.OrdinalIgnoreCase);

builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.Stateless = true;
        options.ConfigureSessionOptions = (httpContext, mcpOptions, _) =>
        {
            string path = httpContext.Request.Path.Value ?? string.Empty;
            string serverKey = ResolveServerKey(path);

            if (!toolDictionary.TryGetValue(serverKey, out McpServerTool[]? tools))
            {
                return Task.CompletedTask;
            }

            mcpOptions.ToolCollection = [];
            foreach (McpServerTool tool in tools)
            {
                mcpOptions.ToolCollection.Add(tool);
            }

            return Task.CompletedTask;
        };
    });

var app = builder.Build();

ServiceCollectionExtensions.PopulateToolDictionary(app.Services, toolDictionary);

app.UseMiddleware<McpAgentRoleMiddleware>();

app.MapMcp("/knowledge-search/mcp");
app.MapMcp("/curation-compliance/mcp");
app.MapMcp("/raw-source/mcp");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

static string ResolveServerKey(string path)
{
    if (path.Contains("/knowledge-search/", StringComparison.OrdinalIgnoreCase))
    {
        return "knowledge-search";
    }

    if (path.Contains("/curation-compliance/", StringComparison.OrdinalIgnoreCase))
    {
        return "curation-compliance";
    }

    if (path.Contains("/raw-source/", StringComparison.OrdinalIgnoreCase))
    {
        return "raw-source";
    }

    return "knowledge-search";
}
