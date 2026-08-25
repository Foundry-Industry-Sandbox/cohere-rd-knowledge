using System.Text.Json;
using System.Text.Json.Nodes;

namespace RndKnowledgeMining.Mcp.Adapters;

internal static class NormalizedDocumentHandoffSplitter
{
    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        WriteIndented = false
    };

    internal sealed record SplitResult(
        string ManifestJson,
        IReadOnlyList<NormalizedDocumentArtifact> Documents);

    internal sealed record NormalizedDocumentArtifact(
        string DocumentId,
        string? SourceFile,
        string? CanonicalType,
        string DocumentJson);

    public static SplitResult Split(string ingestionPayloadJson)
    {
        using JsonDocument document = JsonDocument.Parse(ingestionPayloadJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Ingestion payload must be a JSON object.");
        }

        JsonElement root = document.RootElement;
        var artifacts = new List<NormalizedDocumentArtifact>();

        if (TryGetProperty(root, "documents", out JsonElement documentsElement)
            && documentsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement documentElement in documentsElement.EnumerateArray())
            {
                if (documentElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string documentId = ReadDocumentId(documentElement) ?? $"doc-{artifacts.Count + 1}";
                string? sourceFile = ReadOptionalString(documentElement, "sourceFile");
                string? canonicalType = ReadOptionalString(documentElement, "canonicalType");
                artifacts.Add(new NormalizedDocumentArtifact(
                    documentId,
                    sourceFile,
                    canonicalType,
                    documentElement.GetRawText()));
            }
        }

        JsonObject manifest = BuildManifest(root, artifacts);
        return new SplitResult(manifest.ToJsonString(CompactJsonOptions), artifacts);
    }

    private static JsonObject BuildManifest(JsonElement root, IReadOnlyList<NormalizedDocumentArtifact> artifacts)
    {
        var manifest = new JsonObject
        {
            ["handoffMode"] = "normalized-storage",
            ["storageLayout"] = "per-document"
        };

        CopyIfPresent(root, manifest, "batchId");
        CopyIfPresent(root, manifest, "ingestionRunId");
        CopyIfPresent(root, manifest, "source");
        CopyIfPresent(root, manifest, "summary");
        CopyIfPresent(root, manifest, "normalizedEntitiesMentioned");
        CopyIfPresent(root, manifest, "exclusions");
        CopyIfPresent(root, manifest, "reviewFlags");

        var documentSummaries = new JsonArray();
        foreach (NormalizedDocumentArtifact artifact in artifacts)
        {
            var summary = new JsonObject
            {
                ["documentId"] = artifact.DocumentId,
                ["storageFileName"] = NormalizedDocumentPathBuilder.ToStorageFileName(artifact.DocumentId)
            };

            if (!string.IsNullOrWhiteSpace(artifact.SourceFile))
            {
                summary["sourceFile"] = artifact.SourceFile;
            }

            if (!string.IsNullOrWhiteSpace(artifact.CanonicalType))
            {
                summary["canonicalType"] = artifact.CanonicalType;
            }

            documentSummaries.Add(summary);
        }

        manifest["documentIds"] = new JsonArray(
            artifacts.Select(artifact => JsonValue.Create(artifact.DocumentId)).ToArray());
        manifest["documentsReceived"] = artifacts.Count;
        manifest["documentSummaries"] = documentSummaries;

        return manifest;
    }

    private static string? ReadDocumentId(JsonElement documentElement)
    {
        string? documentId = ReadOptionalString(documentElement, "documentId");
        return string.IsNullOrWhiteSpace(documentId) ? null : documentId.Trim();
    }

    private static void CopyIfPresent(JsonElement root, JsonObject target, string propertyName)
    {
        if (TryGetProperty(root, propertyName, out JsonElement value))
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
}

internal static class NormalizedDocumentPathBuilder
{
    public static string BuildBatchRoot(string normalizedRoot, string sourceId, string executionId) =>
        $"{normalizedRoot.TrimEnd('/')}/{sourceId.Trim()}/{executionId.Trim()}";

    public static string BuildManifestRelativePath(string normalizedRoot, string sourceId, string executionId) =>
        $"{BuildBatchRoot(normalizedRoot, sourceId, executionId)}/manifest.json";

    public static string BuildDocumentRelativePath(string normalizedRoot, string sourceId, string executionId, string documentId) =>
        $"{BuildBatchRoot(normalizedRoot, sourceId, executionId)}/documents/{ToStorageFileName(documentId)}";

    public static string ToStorageFileName(string documentId)
    {
        string sanitized = string.Concat(documentId.Select(ch =>
            Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

        return $"{sanitized}.json";
    }

    public static string BuildLocalBatchDirectory(string datasetRoot, string sourceId, string executionId) =>
        Path.Combine(datasetRoot, "cases", sourceId, "normalized", executionId);

    public static string BuildLocalManifestPath(string datasetRoot, string sourceId, string executionId) =>
        Path.Combine(BuildLocalBatchDirectory(datasetRoot, sourceId, executionId), "manifest.json");

    public static string BuildLocalDocumentPath(string datasetRoot, string sourceId, string executionId, string documentId) =>
        Path.Combine(
            BuildLocalBatchDirectory(datasetRoot, sourceId, executionId),
            "documents",
            ToStorageFileName(documentId));
}
