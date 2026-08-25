using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CohereRndKnowledge.AgentProvisioning.Models;

namespace CohereRndKnowledge.AgentProvisioning;

public sealed class AgentDefinitionBuilder
{
    private const string MetadataLinkingAgentName = "metadata-linking-agent";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    public string BuildDefinitionJson(AgentAssetBundle bundle, ProvisioningSettings settings)
    {
        string serverUrl = $"{settings.McpBaseUrl.TrimEnd('/')}{NormalizePath(bundle.Mcp.Path)}";
        string modelDeploymentName = string.Equals(
            bundle.Manifest.Name,
            MetadataLinkingAgentName,
            StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(settings.MetadataModelDeploymentName)
            ? settings.MetadataModelDeploymentName
            : settings.ModelDeploymentName;

        JsonObject definition = new()
        {
            ["kind"] = "prompt",
            ["model"] = modelDeploymentName,
            ["instructions"] = BuildInstructions(bundle),
            ["temperature"] = 0.2,
            ["tools"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "mcp",
                    ["server_label"] = bundle.Mcp.ServerLabel,
                    ["server_url"] = serverUrl,
                    ["require_approval"] = "never",
                    ["headers"] = new JsonObject
                    {
                        ["X-Agent-Role"] = bundle.Manifest.Name
                    }
                }
            }
        };

        if (AgentAssetLoader.UsesInstructionsOnlyOutput(bundle.Manifest))
        {
            definition["max_tokens"] =
                AgentModelLimits.GetInstructionOnlyOutputTokenBudget(bundle.Manifest.Name);
        }

        if (!AgentAssetLoader.UsesInstructionsOnlyOutput(bundle.Manifest))
        {
            JsonObject textFormat = AgentAssetLoader.UsesJsonObjectOutput(bundle.Manifest)
                ? new JsonObject { ["type"] = "json_object" }
                : new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = "AgentStructuredOutput",
                    ["description"] = "Structured agent output consumed by the R&D knowledge mining workflow API.",
                    ["schema"] = JsonNode.Parse(bundle.OutputSchemaJson),
                    ["strict"] = true
                };

            definition["text"] = new JsonObject
            {
                ["format"] = textFormat
            };
        }

        return definition.ToJsonString(SerializerOptions);
    }

    public string BuildCreateVersionRequestJson(
        string definitionJson,
        AgentManifest manifest,
        string fingerprintMetadataKey,
        string definitionFingerprint)
    {
        JsonObject request = new()
        {
            ["definition"] = JsonNode.Parse(definitionJson),
            ["description"] = manifest.Description,
            ["metadata"] = new JsonObject
            {
                [fingerprintMetadataKey] = definitionFingerprint
            }
        };

        return request.ToJsonString(SerializerOptions);
    }

    public string ComputeFingerprint(AgentAssetBundle bundle, string definitionJson)
    {
        StringBuilder builder = new();
        builder.Append(definitionJson);
        builder.Append('\n');
        builder.Append(bundle.GovernancePolicyYaml);
        builder.Append('\n');
        builder.Append(bundle.GovernanceRogueYaml);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash);
    }

    private static string BuildInstructions(AgentAssetBundle bundle)
    {
        StringBuilder builder = new();
        builder.AppendLine(bundle.Instructions);
        builder.AppendLine();
        builder.AppendLine("## Structured Output Contract");
        if (AgentAssetLoader.UsesInstructionsOnlyOutput(bundle.Manifest)
            || AgentAssetLoader.UsesJsonObjectOutput(bundle.Manifest))
        {
            builder.AppendLine("Return a single JSON object matching the output structure defined in these instructions.");
            builder.AppendLine("The workflow API passes the full JSON object to downstream agents and the Knowledge Curator gate after metadata-linking indexes to the Vector DB.");
            builder.AppendLine();
            builder.AppendLine("Formatting rules:");
            builder.AppendLine("- Return raw JSON only on your final response after all MCP tool calls complete. Do not wrap the JSON in markdown code fences.");
            builder.AppendLine("- Do not include extra text before or after the JSON.");
            builder.AppendLine("- Keep reasoning concise and reserve enough of the shared output budget to complete the final JSON object.");
            builder.AppendLine("- Do not emit tool-call JSON as your final answer; use MCP tools for reads and indexing, then return the completed handoff JSON object.");
            builder.AppendLine("- Domain fields (documents, entities, links, etc.) are defined above, not by a separate JSON Schema.");
        }
        else if (UsesSearchChatStructuredOutput(bundle))
        {
            builder.AppendLine("Return JSON only with these required properties: summary, decision, evidence, citations, lineage, raw_source_trace.");
            builder.AppendLine("The API requires all of these properties.");
            builder.AppendLine();
            builder.AppendLine("Formatting rules:");
            builder.AppendLine("- Return raw JSON only. Do not wrap the JSON in markdown code fences.");
            builder.AppendLine("- Do not include extra text before or after the JSON.");
            builder.AppendLine("- evidence must be a plain string, not an object or array.");
            builder.AppendLine("- citations must be an array of strings. Use an empty array when none apply.");
            builder.AppendLine("- lineage must be a plain string. Use an empty string when traceability is not relevant.");
            builder.AppendLine("- raw_source_trace must be true only when the answer is grounded in persisted knowledge-base entities.");
        }
        else if (UsesCurationComplianceStructuredOutput(bundle))
        {
            builder.AppendLine("Return JSON only with these required properties: summary, decision, evidence, flags, capturedDecisions, policyRefs, citations, sensitive_content_found, required_human_review.");
            builder.AppendLine("The API requires all of these properties.");
            builder.AppendLine();
            builder.AppendLine("Formatting rules:");
            builder.AppendLine("- Return raw JSON only. Do not wrap the JSON in markdown code fences.");
            builder.AppendLine("- Do not include extra text before or after the JSON.");
            builder.AppendLine("- evidence must be a plain string, not an object or array.");
            builder.AppendLine("- flags, capturedDecisions, policyRefs, and citations must be arrays of strings. Use empty arrays when none apply.");
            builder.AppendLine("- sensitive_content_found and required_human_review must be booleans.");
        }
        else
        {
            builder.AppendLine("Return JSON only with these required properties: summary, decision, evidence.");
            builder.AppendLine("The API requires summary, decision, and evidence.");
            builder.AppendLine();
            builder.AppendLine("Formatting rules:");
            builder.AppendLine("- Return raw JSON only. Do not wrap the JSON in markdown code fences.");
            builder.AppendLine("- Do not include extra text before or after the JSON.");
            builder.AppendLine("- evidence must be a plain string, not an object or array.");
        }
        builder.AppendLine();
        builder.AppendLine("## Allowed Decision Values");
        foreach (string decision in bundle.Manifest.AllowedDecisions)
        {
            builder.AppendLine($"- {decision}");
        }

        builder.AppendLine();
        builder.AppendLine("## Workflow Boundaries");
        builder.AppendLine("Consume prior workflow outputs when provided. Do not repeat work owned by earlier agents.");
        builder.AppendLine("Produce recommendations and evidence only. Human-in-the-loop orchestration is handled by the workflow, not by this agent.");

        return builder.ToString().Trim();
    }

    private static bool UsesSearchChatStructuredOutput(AgentAssetBundle bundle) =>
        bundle.OutputSchemaJson.Contains("\"raw_source_trace\"", StringComparison.Ordinal);

    private static bool UsesCurationComplianceStructuredOutput(AgentAssetBundle bundle) =>
        bundle.OutputSchemaJson.Contains("\"sensitive_content_found\"", StringComparison.Ordinal);

    private static string NormalizePath(string path)
    {
        return path.StartsWith('/') ? path : $"/{path}";
    }
}
