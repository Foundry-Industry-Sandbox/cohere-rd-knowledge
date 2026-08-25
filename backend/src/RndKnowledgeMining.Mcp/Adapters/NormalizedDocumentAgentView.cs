using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RndKnowledgeMining.Mcp.Adapters;

/// <summary>
/// Compacts persisted normalized documents before returning them to agents via MCP,
/// keeping linking metadata while avoiding multi-document context blowups.
/// </summary>
public static class NormalizedDocumentAgentView
{
    private const int MaxAbstractLength = 1_200;
    private const int MaxSectionTextLength = 800;
    private const int MaxSections = 12;
    private const int MaxReferences = 8;
    private const int MaxReferenceLength = 300;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Compact(string documentJson)
    {
        if (string.IsNullOrWhiteSpace(documentJson))
        {
            return documentJson;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(documentJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return TruncateRaw(documentJson);
            }

            JsonElement root = document.RootElement;
            var compact = new JsonObject
            {
                ["format"] = "normalized-document-agent-view",
                ["documentId"] = ReadString(root, "documentId"),
                ["sourceFile"] = ReadString(root, "sourceFile"),
                ["sourceType"] = ReadString(root, "sourceType"),
                ["canonicalType"] = ReadString(root, "canonicalType"),
                ["title"] = ReadString(root, "title"),
                ["published"] = ReadString(root, "published"),
                ["license"] = ReadString(root, "license"),
                ["language"] = ReadString(root, "language")
            };

            if (TryGetProperty(root, "identifiers", out JsonElement identifiers)
                && identifiers.ValueKind == JsonValueKind.Object)
            {
                compact["identifiers"] = JsonNode.Parse(identifiers.GetRawText());
            }

            if (TryGetProperty(root, "authors", out JsonElement authors) && authors.ValueKind == JsonValueKind.Array)
            {
                compact["authors"] = BuildTrimmedStringArray(authors, maxItems: 20, maxLength: 120);
            }

            if (TryGetProperty(root, "flags", out JsonElement flags) && flags.ValueKind == JsonValueKind.Array)
            {
                compact["flags"] = JsonNode.Parse(flags.GetRawText());
            }

            if (TryGetProperty(root, "dedup", out JsonElement dedup) && dedup.ValueKind == JsonValueKind.Object)
            {
                compact["dedup"] = JsonNode.Parse(dedup.GetRawText());
            }

            compact["sections"] = BuildSections(root);
            compact["extractedReferences"] = BuildReferences(root);
            compact["agentViewNotes"] = new JsonObject
            {
                ["guidance"] = "Use this compact view for linking and chunk planning. Full text is persisted in storage and should be indexed via index_rd_knowledge_batch, not echoed in your final JSON.",
                ["originalBytes"] = documentJson.Length,
                ["compactBytes"] = 0
            };

            string result = compact.ToJsonString(JsonOptions);
            compact["agentViewNotes"]!["compactBytes"] = result.Length;
            return compact.ToJsonString(JsonOptions);
        }
        catch (JsonException)
        {
            return TruncateRaw(documentJson);
        }
    }

    private static JsonArray BuildSections(JsonElement root)
    {
        var sections = new JsonArray();

        string? abstractText = ReadString(root, "abstract");
        if (!string.IsNullOrWhiteSpace(abstractText))
        {
            sections.Add(new JsonObject
            {
                ["sectionId"] = "abstract",
                ["title"] = "Abstract",
                ["text"] = Truncate(abstractText, MaxAbstractLength)
            });
        }

        if (!TryGetProperty(root, "sections", out JsonElement sectionElements)
            || sectionElements.ValueKind != JsonValueKind.Array)
        {
            return sections;
        }

        int count = 0;
        foreach (JsonElement sectionElement in sectionElements.EnumerateArray())
        {
            if (sectionElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? text = ReadString(sectionElement, "text");
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            sections.Add(new JsonObject
            {
                ["sectionId"] = ReadString(sectionElement, "sectionId") ?? ReadString(sectionElement, "title") ?? $"section-{count + 1}",
                ["title"] = ReadString(sectionElement, "title") ?? "Section",
                ["text"] = Truncate(text, MaxSectionTextLength)
            });

            count++;
            if (count >= MaxSections)
            {
                break;
            }
        }

        return sections;
    }

    private static JsonArray BuildReferences(JsonElement root)
    {
        var references = new JsonArray();
        if (!TryGetProperty(root, "extractedReferences", out JsonElement referenceElements)
            || referenceElements.ValueKind != JsonValueKind.Array)
        {
            return references;
        }

        int count = 0;
        foreach (JsonElement referenceElement in referenceElements.EnumerateArray())
        {
            string? citation = referenceElement.ValueKind == JsonValueKind.String
                ? referenceElement.GetString()
                : ReadString(referenceElement, "citation");

            if (string.IsNullOrWhiteSpace(citation))
            {
                continue;
            }

            references.Add(Truncate(citation, MaxReferenceLength));
            count++;
            if (count >= MaxReferences)
            {
                break;
            }
        }

        return references;
    }

    private static JsonArray BuildTrimmedStringArray(JsonElement array, int maxItems, int maxLength)
    {
        var values = new JsonArray();
        int count = 0;
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string? value = item.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            values.Add(Truncate(value, maxLength));
            count++;
            if (count >= maxItems)
            {
                break;
            }
        }

        return values;
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement value)
    {
        if (root.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "...");

    private static string TruncateRaw(string value) =>
        value.Length <= ToolResponseLimits.MaxNormalizedDocumentResponseChars
            ? value
            : string.Concat(value.AsSpan(0, ToolResponseLimits.MaxNormalizedDocumentResponseChars), "\n...[TRUNCATED]");
}
