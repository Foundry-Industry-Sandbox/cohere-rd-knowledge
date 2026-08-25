using System.Text.Json;
using System.Text.Json.Serialization;

namespace CohereRndKnowledgeMining.Api.Host.Workflow;

public static class AgentWorkflowAgents
{
    public const string IngestionTranslation = "ingestion-translation-agent";

    public const string MetadataLinking = "metadata-linking-agent";

    public static bool UsesRichPayload(string agentName) =>
        string.Equals(agentName, IngestionTranslation, StringComparison.OrdinalIgnoreCase)
        || string.Equals(agentName, MetadataLinking, StringComparison.OrdinalIgnoreCase);
}

public sealed class NormalizedDocument
{
    [JsonPropertyName("documentId")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("sourceItemId")]
    public required string SourceItemId { get; init; }

    [JsonPropertyName("sourceType")]
    public required string SourceType { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("canonicalKey")]
    public required string CanonicalKey { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }
}

/// <summary>
/// Structured output contract expected from each Foundry agent at step completion.
/// Agents must be provisioned externally to return JSON matching this shape.
/// </summary>
public sealed class AgentStructuredOutput
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("decision")]
    public required string Decision { get; init; }

    [JsonPropertyName("evidence")]
    public required string Evidence { get; init; }

    [JsonPropertyName("riskLevel")]
    public string? RiskLevel { get; init; }

    [JsonPropertyName("policyRefs")]
    public IReadOnlyList<string>? PolicyRefs { get; init; }

    [JsonPropertyName("anomalies")]
    public IReadOnlyList<string>? Anomalies { get; init; }

    [JsonPropertyName("keyFacts")]
    public IReadOnlyList<string>? KeyFacts { get; init; }

    [JsonPropertyName("flags")]
    public IReadOnlyList<string>? Flags { get; init; }

    [JsonPropertyName("citations")]
    public IReadOnlyList<string>? Citations { get; init; }

    [JsonPropertyName("lineage")]
    public string? Lineage { get; init; }

    [JsonPropertyName("capturedDecisions")]
    public IReadOnlyList<string>? CapturedDecisions { get; init; }

    [JsonPropertyName("documentsProcessed")]
    public int? DocumentsProcessed { get; init; }

    [JsonPropertyName("duplicatesRemoved")]
    public int? DuplicatesRemoved { get; init; }

    [JsonPropertyName("normalizedFormats")]
    public IReadOnlyList<string>? NormalizedFormats { get; init; }

    [JsonPropertyName("normalizedDocuments")]
    public IReadOnlyList<NormalizedDocument>? NormalizedDocuments { get; init; }
}

public sealed class AgentStepResult
{
    public required string AgentName { get; init; }

    public required string Summary { get; init; }

    public required string Decision { get; init; }

    public required string Evidence { get; init; }

    public string? RiskLevel { get; init; }

    public IReadOnlyList<string>? PolicyRefs { get; init; }

    public IReadOnlyList<string>? Anomalies { get; init; }

    public IReadOnlyList<string>? KeyFacts { get; init; }

    public IReadOnlyList<string>? Flags { get; init; }

    public IReadOnlyList<string>? Citations { get; init; }

    public string? Lineage { get; init; }

    public IReadOnlyList<string>? CapturedDecisions { get; init; }

    public int? DocumentsProcessed { get; init; }

    public int? DuplicatesRemoved { get; init; }

    public IReadOnlyList<string>? NormalizedFormats { get; init; }

    public IReadOnlyList<NormalizedDocument>? NormalizedDocuments { get; init; }

    /// <summary>Full agent JSON for Block 1 rich payloads passed through workflow hand-offs.</summary>
    public string? RawPayloadJson { get; init; }

    public required DateTimeOffset CompletedAtUtc { get; init; }

    public static AgentStepResult FromStructuredOutput(string agentName, AgentStructuredOutput output) =>
        new()
        {
            AgentName = agentName,
            Summary = output.Summary,
            Decision = output.Decision,
            Evidence = output.Evidence,
            RiskLevel = output.RiskLevel,
            PolicyRefs = output.PolicyRefs,
            Anomalies = output.Anomalies,
            KeyFacts = output.KeyFacts,
            Flags = output.Flags,
            Citations = output.Citations,
            Lineage = output.Lineage,
            CapturedDecisions = output.CapturedDecisions,
            DocumentsProcessed = output.DocumentsProcessed,
            DuplicatesRemoved = output.DuplicatesRemoved,
            NormalizedFormats = output.NormalizedFormats,
            NormalizedDocuments = output.NormalizedDocuments,
            RawPayloadJson = null,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };

    public static AgentStepResult FromRichPayload(
        string agentName,
        string rawPayloadJson,
        string summary,
        string decision,
        string evidence) =>
        new()
        {
            AgentName = agentName,
            Summary = summary,
            Decision = decision,
            Evidence = evidence,
            RawPayloadJson = rawPayloadJson,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
}

public static class AgentStructuredOutputParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static AgentStepResult Parse(string agentName, string rawOutput)
    {
        if (AgentWorkflowAgents.UsesRichPayload(agentName))
        {
            return ParseRichPayload(agentName, rawOutput);
        }

        AgentStructuredOutput? structured = TryParseStructuredOutput(rawOutput);
        if (structured is not null)
        {
            return AgentStepResult.FromStructuredOutput(agentName, structured);
        }

        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            throw new InvalidOperationException(
                $"Agent '{agentName}' returned empty output. Expected JSON with summary, decision, and evidence.");
        }

        string trimmedOutput = rawOutput.Trim();
        if (trimmedOutput.Contains("Error (", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Agent '{agentName}' returned an error instead of structured JSON: {Truncate(trimmedOutput)}");
        }

        throw new InvalidOperationException(
            $"Agent '{agentName}' did not return valid structured JSON. Expected properties: summary, decision, evidence.");
    }

    private static AgentStepResult ParseRichPayload(string agentName, string rawOutput)
    {
        if (TryParseRichPayloadCore(agentName, rawOutput, out AgentStepResult result))
        {
            return result;
        }

        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            throw new InvalidOperationException(
                $"Agent '{agentName}' returned empty output. Expected a JSON object.");
        }

        string trimmedOutput = rawOutput.Trim();
        if (trimmedOutput.Contains("Error (", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Agent '{agentName}' returned an error instead of JSON: {Truncate(trimmedOutput)}");
        }

        throw new InvalidOperationException(
            LooksLikeIncompleteToolRound(trimmedOutput)
                ? $"Agent '{agentName}' returned MCP tool-call output instead of the completed JSON handoff payload. " +
                  "The agent must finish all MCP tool rounds and emit the final handoff JSON before the workflow bridge forwards to the next step."
                : $"Agent '{agentName}' did not return a recognized JSON handoff payload.");
    }

    private static bool LooksLikeIncompleteToolRound(string rawOutput) =>
        rawOutput.Contains("tool_call_id", StringComparison.OrdinalIgnoreCase)
        || rawOutput.Contains("tool_name", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseRichPayloadCore(string agentName, string rawOutput, out AgentStepResult result)
    {
        result = null!;

        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            return false;
        }

        if (rawOutput.Trim().Contains("Error (", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (string candidate in CollectJsonCandidates(rawOutput))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(candidate);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                JsonElement root = document.RootElement;
                if (!IsRecognizedHandoffPayload(agentName, root))
                {
                    continue;
                }

                ValidateRichPayloadConsistency(agentName, root);

                string summary = DeriveRichSummary(agentName, root);
                string decision = DeriveRichDecision(agentName, root);
                string evidence = DeriveRichEvidence(root);
                string canonicalJson = JsonSerializer.Serialize(root, JsonOptions);
                result = AgentStepResult.FromRichPayload(agentName, canonicalJson, summary, decision, evidence);
                return true;
            }
            catch (JsonException)
            {
                // try next candidate
            }
        }

        return false;
    }

    private static void ValidateRichPayloadConsistency(string agentName, JsonElement root)
    {
        if (!string.Equals(agentName, AgentWorkflowAgents.MetadataLinking, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        int vectorsIndexed = ReadOptionalInt(root, "vectorsIndexed") ?? 0;
        int chunksToEmbed = 0;

        if (TryGetPropertyIgnoreCase(root, "embeddingPlan", out JsonElement embeddingPlan)
            && embeddingPlan.ValueKind == JsonValueKind.Object)
        {
            chunksToEmbed = ReadOptionalInt(embeddingPlan, "chunksToEmbed") ?? 0;
        }

        // metadata-linking must index when it declares embeddable chunks.
        if (chunksToEmbed > 0 && vectorsIndexed <= 0)
        {
            throw new InvalidOperationException(
                "metadata-linking-agent reported embeddable chunks but did not index any vectors. " +
                "Expected at least one index_rd_knowledge or index_rd_knowledge_batch call when embeddingPlan.chunksToEmbed > 0.");
        }

        string? decision = ReadOptionalString(root, "decision");
        if (chunksToEmbed > 0
            && vectorsIndexed < chunksToEmbed
            && string.Equals(decision, "Linking Complete", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "metadata-linking-agent returned decision 'Linking Complete' with partial indexing " +
                "(vectorsIndexed < embeddingPlan.chunksToEmbed).");
        }
    }

    private static bool IsRecognizedHandoffPayload(string agentName, JsonElement root)
    {
        if (string.Equals(agentName, AgentWorkflowAgents.IngestionTranslation, StringComparison.OrdinalIgnoreCase))
        {
            bool hasBatchIdentity = TryGetPropertyIgnoreCase(root, "batchId", out _)
                || TryGetPropertyIgnoreCase(root, "ingestionRunId", out _);
            bool hasIngestionContent =
                (TryGetPropertyIgnoreCase(root, "documents", out JsonElement documents)
                 && documents.ValueKind == JsonValueKind.Array)
                || (TryGetPropertyIgnoreCase(root, "documentSummaries", out JsonElement documentSummaries)
                    && documentSummaries.ValueKind == JsonValueKind.Array)
                || (TryGetPropertyIgnoreCase(root, "summary", out JsonElement summary)
                    && summary.ValueKind == JsonValueKind.Object)
                || (TryGetPropertyIgnoreCase(root, "normalizedEntitiesMentioned", out JsonElement entities)
                    && entities.ValueKind == JsonValueKind.Array);

            return hasBatchIdentity && hasIngestionContent;
        }

        if (string.Equals(agentName, AgentWorkflowAgents.MetadataLinking, StringComparison.OrdinalIgnoreCase))
        {
            bool hasRunIdentity = TryGetPropertyIgnoreCase(root, "batchId", out _)
                || TryGetPropertyIgnoreCase(root, "metadataRunId", out _);
            bool hasLinkingContent =
                (TryGetPropertyIgnoreCase(root, "entities", out JsonElement entities)
                 && entities.ValueKind == JsonValueKind.Array)
                || (TryGetPropertyIgnoreCase(root, "links", out JsonElement links)
                    && links.ValueKind == JsonValueKind.Array)
                || (TryGetPropertyIgnoreCase(root, "embeddingPlan", out JsonElement embeddingPlan)
                    && embeddingPlan.ValueKind == JsonValueKind.Object
                    && embeddingPlan.EnumerateObject().Any());

            return hasRunIdentity && hasLinkingContent;
        }

        return true;
    }

    private static string DeriveRichSummary(string agentName, JsonElement root)
    {
        if (TryGetPropertyIgnoreCase(root, "summary", out JsonElement summaryElement))
        {
            if (summaryElement.ValueKind == JsonValueKind.String)
            {
                string? text = summaryElement.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            else if (summaryElement.ValueKind == JsonValueKind.Object)
            {
                int? accepted = ReadOptionalInt(summaryElement, "documentsAccepted");
                int? received = ReadOptionalInt(summaryElement, "documentsReceived");
                if (accepted.HasValue && received.HasValue)
                {
                    return $"{accepted.Value}/{received.Value} documents accepted";
                }

                return summaryElement.GetRawText();
            }
        }

        if (TryGetPropertyIgnoreCase(root, "batchId", out JsonElement batchId)
            && batchId.ValueKind == JsonValueKind.String)
        {
            string? batch = batchId.GetString();
            if (!string.IsNullOrWhiteSpace(batch))
            {
                return $"Batch {batch}";
            }
        }

        return $"{agentName} completed";
    }

    private static string DeriveRichDecision(string agentName, JsonElement root)
    {
        string? explicitDecision = ReadOptionalString(root, "decision");
        if (!string.IsNullOrWhiteSpace(explicitDecision))
        {
            return explicitDecision;
        }

        if (HasReviewFlagAtOrAbove(root, "high"))
        {
            return agentName switch
            {
                AgentWorkflowAgents.IngestionTranslation => "Human Review Needed",
                AgentWorkflowAgents.MetadataLinking => "Human Review Needed",
                _ => "Human Review Needed"
            };
        }

        return agentName switch
        {
            AgentWorkflowAgents.IngestionTranslation => "Ingestion Complete",
            AgentWorkflowAgents.MetadataLinking => "Linking Complete",
            _ => "Complete"
        };
    }

    private static string DeriveRichEvidence(JsonElement root)
    {
        string? explicitEvidence = ReadEvidence(root, "evidence");
        if (!string.IsNullOrWhiteSpace(explicitEvidence))
        {
            return explicitEvidence;
        }

        if (TryGetPropertyIgnoreCase(root, "reviewFlags", out JsonElement reviewFlags)
            && reviewFlags.ValueKind == JsonValueKind.Array
            && reviewFlags.GetArrayLength() > 0)
        {
            return reviewFlags.GetRawText();
        }

        return "See full agent payload.";
    }

    private static bool HasReviewFlagAtOrAbove(JsonElement root, string minimumSeverity)
    {
        if (!TryGetPropertyIgnoreCase(root, "reviewFlags", out JsonElement reviewFlags)
            || reviewFlags.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (JsonElement flag in reviewFlags.EnumerateArray())
        {
            if (flag.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? severity = ReadOptionalString(flag, "severity");
            if (string.IsNullOrWhiteSpace(severity))
            {
                continue;
            }

            if (string.Equals(severity, "high", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(minimumSeverity, "medium", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(severity, "medium", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    public static AgentStructuredOutput? TryParseStructuredOutput(string rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            return null;
        }

        string trimmedOutput = rawOutput.Trim();
        if (trimmedOutput.Contains("Error (", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (string candidate in CollectJsonCandidates(rawOutput))
        {
            AgentStructuredOutput? parsed = TryParseStructuredOutputFromJson(candidate);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        return null;
    }

    public static AgentStepResult? TryParse(string agentName, string rawOutput)
    {
        AgentStructuredOutput? structured = TryParseStructuredOutput(rawOutput);
        return structured is null
            ? null
            : AgentStepResult.FromStructuredOutput(agentName, structured);
    }

    private static AgentStructuredOutput? TryParseStructuredOutputFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        AgentStructuredOutput? strict = TryDeserializeStrict(json);
        if (strict is not null &&
            !string.IsNullOrWhiteSpace(strict.Summary) &&
            !string.IsNullOrWhiteSpace(strict.Decision) &&
            !string.IsNullOrWhiteSpace(strict.Evidence))
        {
            return strict;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            JsonElement root = document.RootElement;
            string? summary = ReadRequiredString(root, "summary");
            string? decision = ReadRequiredString(root, "decision");
            string? evidence = ReadEvidence(root, "evidence");

            if (string.IsNullOrWhiteSpace(summary) ||
                string.IsNullOrWhiteSpace(decision) ||
                string.IsNullOrWhiteSpace(evidence))
            {
                return null;
            }

            return new AgentStructuredOutput
            {
                Summary = summary,
                Decision = decision,
                Evidence = evidence,
                RiskLevel = ReadOptionalString(root, "riskLevel"),
                PolicyRefs = ReadOptionalStringArray(root, "policyRefs"),
                Anomalies = ReadOptionalStringArray(root, "anomalies"),
                KeyFacts = ReadOptionalStringArray(root, "keyFacts"),
                Flags = ReadOptionalStringArray(root, "flags"),
                Citations = ReadOptionalStringArray(root, "citations"),
                Lineage = ReadOptionalString(root, "lineage"),
                CapturedDecisions = ReadOptionalStringArray(root, "capturedDecisions"),
                DocumentsProcessed = ReadOptionalInt(root, "documentsProcessed"),
                DuplicatesRemoved = ReadOptionalInt(root, "duplicatesRemoved"),
                NormalizedFormats = ReadOptionalStringArray(root, "normalizedFormats"),
                NormalizedDocuments = ReadOptionalNormalizedDocuments(root, "normalizedDocuments")
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AgentStructuredOutput? TryDeserializeStrict(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<AgentStructuredOutput>(text, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadRequiredString(JsonElement root, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(root, propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.GetRawText();
    }

    private static string? ReadEvidence(JsonElement root, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(root, propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Object or JsonValueKind.Array => value.GetRawText(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.ToString()
        };
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement root, string propertyName, out JsonElement value)
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
        if (!TryGetPropertyIgnoreCase(root, propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.GetRawText();
    }

    private static IReadOnlyList<string>? ReadOptionalStringArray(JsonElement root, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(root, propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<string>();
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                string? text = item.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    items.Add(text);
                }
            }
        }

        return items.Count == 0 ? null : items;
    }

    private static int? ReadOptionalInt(JsonElement root, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(root, propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out int number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out int parsed) => parsed,
            _ => null
        };
    }

    private static IReadOnlyList<NormalizedDocument>? ReadOptionalNormalizedDocuments(
        JsonElement root,
        string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(root, propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<NormalizedDocument>();
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? documentId = ReadRequiredString(item, "documentId");
            string? sourceItemId = ReadRequiredString(item, "sourceItemId");
            string? sourceType = ReadRequiredString(item, "sourceType");
            string? title = ReadRequiredString(item, "title");
            string? canonicalKey = ReadRequiredString(item, "canonicalKey");
            string? status = ReadRequiredString(item, "status");

            if (string.IsNullOrWhiteSpace(documentId) ||
                string.IsNullOrWhiteSpace(sourceItemId) ||
                string.IsNullOrWhiteSpace(sourceType) ||
                string.IsNullOrWhiteSpace(title) ||
                string.IsNullOrWhiteSpace(canonicalKey) ||
                string.IsNullOrWhiteSpace(status))
            {
                continue;
            }

            items.Add(new NormalizedDocument
            {
                DocumentId = documentId,
                SourceItemId = sourceItemId,
                SourceType = sourceType,
                Title = title,
                CanonicalKey = canonicalKey,
                Status = status
            });
        }

        return items.Count == 0 ? null : items;
    }

    private static string NormalizeJsonPayload(string text)
    {
        string normalized = text.Trim();

        if (normalized.StartsWith("```", StringComparison.Ordinal))
        {
            int firstLineBreak = normalized.IndexOf('\n');
            if (firstLineBreak >= 0)
            {
                normalized = normalized[(firstLineBreak + 1)..];
            }

            if (normalized.EndsWith("```", StringComparison.Ordinal))
            {
                normalized = normalized[..^3].TrimEnd();
            }
        }

        return ExtractJsonObject(normalized) ?? normalized.Trim();
    }

    private static IReadOnlyList<string> CollectJsonCandidates(string rawOutput)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void AddCandidate(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            string trimmed = candidate.Trim();
            if (seen.Add(trimmed))
            {
                candidates.Add(trimmed);
            }
        }

        AddCandidate(NormalizeJsonPayload(rawOutput));
        AddCandidate(ExtractJsonObject(rawOutput));

        string normalized = rawOutput.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        const string jsonFence = "```json\n";
        if (normalized.Contains(jsonFence, StringComparison.OrdinalIgnoreCase))
        {
            foreach (string segment in normalized.Split(
                         jsonFence,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int closingFenceIndex = segment.IndexOf("\n```", StringComparison.Ordinal);
                if (closingFenceIndex > 0)
                {
                    AddCandidate(segment[..closingFenceIndex].Trim());
                }
            }
        }

        foreach (string isolated in ExtractAllJsonObjects(rawOutput))
        {
            AddCandidate(isolated);
        }

        return candidates;
    }

    private static IEnumerable<string> ExtractAllJsonObjects(string text)
    {
        for (int start = 0; start < text.Length; start++)
        {
            if (text[start] != '{')
            {
                continue;
            }

            int depth = 0;
            for (int index = start; index < text.Length; index++)
            {
                char current = text[index];
                if (current == '{')
                {
                    depth++;
                }
                else if (current == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        yield return text[start..(index + 1)];
                        break;
                    }
                }
            }
        }
    }

    private static string? ExtractJsonObject(string text)
    {
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');

        if (start < 0 || end <= start)
        {
            return null;
        }

        return text[start..(end + 1)];
    }

    private static string Truncate(string value)
    {
        const int maxLength = 500;
        return value.Length <= maxLength
            ? value
            : string.Concat(value.AsSpan(0, maxLength), "...");
    }
}
