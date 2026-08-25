using System.Text.Json;
using System.Text.Json.Nodes;

namespace RndKnowledgeMining.Mcp.Adapters;

internal static class NormalizedManifestPatcher
{
    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        WriteIndented = false
    };

    /// <summary>
    /// Stamps the workflow execution id onto the manifest so MCP tools and agents share one canonical value.
    /// </summary>
    public static string ApplyWorkflowExecutionId(string manifestJson, string executionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);

        string normalizedExecutionId = NormalizedExecutionIdResolver.Normalize(executionId);
        JsonObject manifest = JsonNode.Parse(manifestJson)!.AsObject();
        manifest["executionId"] = normalizedExecutionId;
        manifest["ingestionRunId"] = normalizedExecutionId;

        return manifest.ToJsonString(CompactJsonOptions);
    }
}
