using System.Text.Json;
using Cohere.AgenticRDKnowledge.Shared.Contracts.Agents;

namespace Cohere.AgenticRDKnowledge.WebApp.Services;

/// <summary>
/// Maps live Api.Host agent structured-output JSON into UI DTOs used by Blazor panels.
/// </summary>
public static class AgentOutputSchemaMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static IngestionTranslationResult? MapIngestionTranslation(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("summary", out var summaryProp))
        {
            return null;
        }

        string? summary;
        int documentsProcessed = 0;
        int anomaliesCount = 0;

        if (summaryProp.ValueKind == JsonValueKind.String)
        {
            // Legacy schema
            summary = summaryProp.GetString();

            if (string.IsNullOrWhiteSpace(summary))
            {
                return null;
            }

            documentsProcessed = ReadStringArray(root, "keyFacts").Count;
            anomaliesCount = ReadStringArray(root, "anomalies").Count;
        }
        else if (summaryProp.ValueKind == JsonValueKind.Object)
        {
            // New schema
            var received = summaryProp.TryGetProperty("documentsReceived", out var receivedProp) &&
                        receivedProp.TryGetInt32(out var receivedCount)
                ? receivedCount
                : 0;

            var accepted = summaryProp.TryGetProperty("documentsAccepted", out var acceptedProp) &&
                        acceptedProp.TryGetInt32(out var acceptedCount)
                ? acceptedCount
                : 0;

            var excluded = summaryProp.TryGetProperty("documentsExcluded", out var excludedProp) &&
                        excludedProp.TryGetInt32(out var excludedCount)
                ? excludedCount
                : 0;

            summary =
                $"Received {received} documents, accepted {accepted}, excluded {excluded}.";

            documentsProcessed = root.TryGetProperty("documentSummaries", out var docsProp) &&
                                docsProp.ValueKind == JsonValueKind.Array
                ? docsProp.GetArrayLength()
                : accepted;

            anomaliesCount = root.TryGetProperty("exclusions", out var exclusionsProp) &&
                            exclusionsProp.ValueKind == JsonValueKind.Array
                ? exclusionsProp.GetArrayLength()
                : excluded;
        }
        else
        {
            return null;
        }

        var flags =
            root.TryGetProperty("reviewFlags", out var reviewFlagsProp) &&
            reviewFlagsProp.ValueKind == JsonValueKind.Array
                ? reviewFlagsProp.EnumerateArray()
                    .Select(x => x.TryGetProperty("code", out var code) ? code.GetString() : null)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!)
                    .ToList()
                : ReadStringArray(root, "flags");

        return new IngestionTranslationResult(
            summary,
            documentsProcessed,
            anomaliesCount,
            ["JSON", "XML", "TXT"],
            flags);
    }

    public static MetadataLinkingResult? MapMetadataLinking(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("summary", out var summaryProp))
        {
            return null;
        }

        var summary = summaryProp.ValueKind switch
        {
            JsonValueKind.String => summaryProp.GetString(),
            JsonValueKind.Object => BuildMetadataLinkingSummary(summaryProp),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        var entities = MapEntityChips(root);
        var links = MapDocumentLinks(root);

        var vectorsIndexed = root.TryGetProperty("vectorsIndexed", out var vectorsProp) && vectorsProp.TryGetInt32(out var count)
            ? count
            : Math.Max(entities.Count, links.Count);

        return new MetadataLinkingResult(summary, entities, links, vectorsIndexed);
    }

    public static CurationComplianceResult? MapCurationCompliance(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("summary", out var summaryProp))
        {
            return null;
        }

        var summary = summaryProp.GetString();
        if (string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        var flagLabels = ReadStringArray(root, "flags");
        var policyRefs = ReadStringArray(root, "policyRefs");
        var capturedDecisions = ReadStringArray(root, "capturedDecisions");

        var complianceFlags = new List<ComplianceFlag>();
        for (int i = 0; i < flagLabels.Count; i++)
        {
            complianceFlags.Add(new ComplianceFlag(
                "Review",
                "Compliance",
                flagLabels[i],
                i < policyRefs.Count ? policyRefs[i] : null));
        }

        return new CurationComplianceResult(summary, complianceFlags, capturedDecisions);
    }

    public static SearchChatResult? MapSearchChat(string json)
    {
        var legacy = JsonSerializer.Deserialize<SearchChatResult>(json, JsonOptions);
        if (legacy is not null && !string.IsNullOrWhiteSpace(legacy.Answer))
        {
            return legacy;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var answer = root.TryGetProperty("answer", out var answerProp) ? answerProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(answer))
        {
            answer = root.TryGetProperty("summary", out var summaryProp) ? summaryProp.GetString() : null;
        }

        if (string.IsNullOrWhiteSpace(answer))
        {
            return null;
        }

        var citations = ReadStringArray(root, "citations")
            .Select((citation, index) => new Citation(
                $"cite-{index + 1}",
                citation,
                citation,
                "Vector DB",
                1.0))
            .ToList();

        var lineage = root.TryGetProperty("evidence", out var evidenceProp) ? evidenceProp.GetString() : null;

        return new SearchChatResult(answer, citations, lineage ?? string.Empty);
    }

    private static string? BuildMetadataLinkingSummary(JsonElement summaryObject)
    {
        if (summaryObject.TryGetProperty("text", out var textProp))
        {
            return textProp.GetString();
        }

        if (summaryObject.TryGetProperty("decision", out var decisionProp))
        {
            return decisionProp.GetString();
        }

        return summaryObject.GetRawText();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var arrayProp) || arrayProp.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return arrayProp.EnumerateArray()
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();
    }

    private static List<EntityChip> MapEntityChips(JsonElement root)
    {
        var entities = new List<EntityChip>();

        if (root.TryGetProperty("entities", out var entitiesProp) &&
            entitiesProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var entity in entitiesProp.EnumerateArray())
            {
                var name =
                    entity.TryGetProperty("canonicalName", out var canonicalProp)
                        ? canonicalProp.GetString()
                        : null;

                var id =
                    entity.TryGetProperty("entityId", out var idProp)
                        ? idProp.GetString()
                        : null;

                // fallback if canonicalName missing
                name ??= id;

                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var category =
                    entity.TryGetProperty("type", out var typeProp)
                        ? typeProp.GetString() ?? "—"
                        : "—";

                entities.Add(new EntityChip(name, category, "—"));
            }
        }

        if (entities.Count == 0)
        {
            foreach (var fact in ReadStringArray(root, "keyFacts"))
            {
                entities.Add(new EntityChip(fact, "Entity", "—"));
            }
        }

        return entities;
    }

    private static List<DocumentLink> MapDocumentLinks(JsonElement root)
    {
        var links = new List<DocumentLink>();
        if (root.TryGetProperty("links", out var linksProp) && linksProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in linksProp.EnumerateArray())
            {
                var from = link.TryGetProperty("from", out var fromProp)
                    ? fromProp.GetString()
                    : link.TryGetProperty("fromDocument", out var fromDocumentProp)
                        ? fromDocumentProp.GetString()
                        : null;
                var to = link.TryGetProperty("to", out var toProp)
                    ? toProp.GetString()
                    : link.TryGetProperty("toTarget", out var toTargetProp)
                        ? toTargetProp.GetString()
                        : null;
                var relationship = link.TryGetProperty("relationship", out var relProp) ? relProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                {
                    continue;
                }

                links.Add(new DocumentLink(from, to, relationship ?? "linked"));
            }
        }

        if (links.Count == 0)
        {
            var linkSources = ReadStringArray(root, "flags")
                .Concat(ReadStringArray(root, "citations"))
                .Concat(ReadStringArray(root, "policyRefs"))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var source in linkSources)
            {
                links.Add(new DocumentLink(source, "Vector DB", "linked"));
            }
        }

        return links;
    }
}
