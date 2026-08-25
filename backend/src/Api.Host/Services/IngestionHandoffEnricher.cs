using System.Text.Json;
using System.Text.Json.Nodes;

namespace CohereRndKnowledgeMining.Api.Host.Services;

/// <summary>
/// Expands manifest-only ingestion handoffs into full per-document payloads using
/// preloaded inline source content before normalized storage persistence.
/// </summary>
public static class IngestionHandoffEnricher
{
    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        WriteIndented = false
    };

    public static string Enrich(string agentPayloadJson, IReadOnlyList<RawKnowledgeItem> sourceItems)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentPayloadJson);
        ArgumentNullException.ThrowIfNull(sourceItems);

        using JsonDocument document = JsonDocument.Parse(agentPayloadJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Ingestion handoff must be a JSON object.");
        }

        JsonElement root = document.RootElement;
        if (HasNonEmptyDocuments(root))
        {
            return agentPayloadJson;
        }

        if (!TryGetProperty(root, "documentSummaries", out JsonElement summariesElement)
            || summariesElement.ValueKind != JsonValueKind.Array
            || summariesElement.GetArrayLength() == 0)
        {
            return agentPayloadJson;
        }

        var sourceByFileName = sourceItems
            .GroupBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var documents = new JsonArray();
        foreach (JsonElement summaryElement in summariesElement.EnumerateArray())
        {
            if (summaryElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? sourceFile = ReadOptionalString(summaryElement, "sourceFile");
            if (string.IsNullOrWhiteSpace(sourceFile)
                || !sourceByFileName.TryGetValue(sourceFile, out RawKnowledgeItem? sourceItem))
            {
                continue;
            }

            JsonObject documentNode = BuildDocumentNode(summaryElement, sourceItem);
            documents.Add(documentNode);
        }

        if (documents.Count == 0)
        {
            throw new InvalidOperationException(
                "Manifest-only ingestion handoff could not be matched to preloaded source documents.");
        }

        JsonObject enriched = JsonNode.Parse(agentPayloadJson)!.AsObject();
        enriched["documents"] = documents;
        enriched.Remove("documentSummaries");
        enriched["handoffMode"] = "normalized-storage-enriched";

        return enriched.ToJsonString(CompactJsonOptions);
    }

    private static bool HasNonEmptyDocuments(JsonElement root) =>
        TryGetProperty(root, "documents", out JsonElement documents)
        && documents.ValueKind == JsonValueKind.Array
        && documents.GetArrayLength() > 0;

    private static JsonObject BuildDocumentNode(JsonElement summaryElement, RawKnowledgeItem sourceItem)
    {
        JsonObject document = CopyObject(summaryElement);
        document["sourceFile"] ??= sourceItem.Title;
        document["sourceType"] ??= sourceItem.SourceType;

        if (TryParsePreparedContent(sourceItem.Content, out JsonElement preparedRoot))
        {
            MergePreparedContent(document, preparedRoot);
        }
        else if (!document.ContainsKey("sections"))
        {
            document["sections"] = new JsonArray
            {
                new JsonObject
                {
                    ["sectionId"] = "body",
                    ["title"] = "Content",
                    ["text"] = Truncate(sourceItem.Content, 1_500)
                }
            };
        }

        return document;
    }

    private static void MergePreparedContent(JsonObject document, JsonElement preparedRoot)
    {
        if (string.Equals(ReadOptionalString(preparedRoot, "format"), "jats-extract", StringComparison.OrdinalIgnoreCase))
        {
            CopyIfMissing(document, preparedRoot, "title");
            CopyIfMissing(document, preparedRoot, "authors");
            CopyIfMissing(document, preparedRoot, "published");
            CopyIfMissing(document, preparedRoot, "license");
            CopyIfMissing(document, preparedRoot, "language");
            CopyIfMissing(document, preparedRoot, "identifiers");

            if (!document.ContainsKey("sections"))
            {
                document["sections"] = BuildSectionsFromJatsExtract(preparedRoot);
            }

            if (!document.ContainsKey("extractedReferences"))
            {
                document["extractedReferences"] = BuildReferencesFromJatsExtract(preparedRoot);
            }

            return;
        }

        if (!document.ContainsKey("sections"))
        {
            document["sections"] = new JsonArray
            {
                new JsonObject
                {
                    ["sectionId"] = "content",
                    ["title"] = "Extracted content",
                    ["text"] = Truncate(preparedRoot.GetRawText(), 1_500)
                }
            };
        }
    }

    private static JsonArray BuildSectionsFromJatsExtract(JsonElement preparedRoot)
    {
        var sections = new JsonArray();

        string? abstractText = ReadOptionalString(preparedRoot, "abstract");
        if (!string.IsNullOrWhiteSpace(abstractText))
        {
            sections.Add(new JsonObject
            {
                ["sectionId"] = "abstract",
                ["title"] = "Abstract",
                ["text"] = Truncate(abstractText, 600)
            });
        }

        if (TryGetProperty(preparedRoot, "sections", out JsonElement sectionElements)
            && sectionElements.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement sectionElement in sectionElements.EnumerateArray())
            {
                if (sectionElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? title = ReadOptionalString(sectionElement, "title") ?? "Section";
                string? text = ReadOptionalString(sectionElement, "text");
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                sections.Add(new JsonObject
                {
                    ["sectionId"] = ReadOptionalString(sectionElement, "sectionId") ?? title,
                    ["title"] = title,
                    ["text"] = Truncate(text, 600)
                });
            }
        }

        return sections;
    }

    private static JsonArray BuildReferencesFromJatsExtract(JsonElement preparedRoot)
    {
        var references = new JsonArray();
        if (!TryGetProperty(preparedRoot, "references", out JsonElement referenceElements)
            || referenceElements.ValueKind != JsonValueKind.Array)
        {
            return references;
        }

        int index = 1;
        foreach (JsonElement referenceElement in referenceElements.EnumerateArray())
        {
            if (referenceElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string? citation = referenceElement.GetString();
            if (string.IsNullOrWhiteSpace(citation))
            {
                continue;
            }

            references.Add(new JsonObject
            {
                ["refId"] = $"ref-{index}",
                ["citation"] = Truncate(citation, 300)
            });
            index++;

            if (index > 10)
            {
                break;
            }
        }

        return references;
    }

    private static bool TryParsePreparedContent(string content, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            root = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static JsonObject CopyObject(JsonElement element)
    {
        JsonObject copy = JsonNode.Parse(element.GetRawText())!.AsObject();
        return copy;
    }

    private static void CopyIfMissing(JsonObject target, JsonElement source, string propertyName)
    {
        if (target.ContainsKey(propertyName))
        {
            return;
        }

        if (TryGetProperty(source, propertyName, out JsonElement value))
        {
            target[propertyName] = JsonNode.Parse(value.GetRawText());
        }
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

    private static string? ReadOptionalString(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "...");
}
