using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace RndKnowledgeMining.Mcp.Adapters;

/// <summary>
/// Converts large PMC JATS XML files into compact agent-friendly JSON so ingestion agents
/// can read every document without blowing context or output token limits.
/// </summary>
internal static class JatsArticleExtractor
{
    private const int MaxAbstractLength = 4_000;
    private const int MaxSectionTextLength = 1_500;
    private const int MaxSections = 25;
    private const int MaxReferences = 12;
    private const int MaxAuthors = 30;
    private const int MaxTableRows = 8;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static bool LooksLikeJatsArticle(string fileName, string content) =>
        fileName.EndsWith("_article.xml", StringComparison.OrdinalIgnoreCase)
        || (fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            && content.Contains("<article", StringComparison.OrdinalIgnoreCase));

    public static string ToAgentJson(string fileName, string rawXml)
    {
        try
        {
            JatsArticleExtract extract = Extract(fileName, rawXml);
            return JsonSerializer.Serialize(extract, JsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                format = "jats-extract-error",
                sourceFile = fileName,
                message = "Failed to parse JATS XML; returning a truncated raw preview.",
                error = ex.Message,
                preview = Truncate(rawXml, 12_000)
            }, JsonOptions);
        }
    }

    private static JatsArticleExtract Extract(string fileName, string rawXml)
    {
        XDocument document = XDocument.Parse(rawXml, LoadOptions.PreserveWhitespace);
        XElement? root = document.Root;
        if (root is null)
        {
            throw new InvalidOperationException("JATS XML has no root element.");
        }

        XElement? articleMeta = FindFirst(root, "article-meta");
        XElement? body = FindFirst(root, "body");

        var identifiers = ReadIdentifiers(articleMeta);
        string? title = ReadText(FindFirst(articleMeta, "article-title"));
        string? journal = ReadText(FindFirst(FindFirst(root, "journal-meta"), "journal-title"));
        string? published = ReadPublicationDate(articleMeta);
        string? license = ReadText(FindFirst(FindFirst(articleMeta, "license"), "license-p"));
        IReadOnlyList<string> authors = ReadAuthors(articleMeta);
        string abstractText = ReadAbstract(articleMeta);
        IReadOnlyList<JatsSectionExtract> sections = ReadSections(body);
        IReadOnlyList<string> references = ReadReferences(root);
        IReadOnlyList<JatsTableExtract> tables = ReadTables(body);

        int originalBytes = rawXml.Length;
        bool sectionsTruncated = sections.Count >= MaxSections;
        bool referencesTruncated = references.Count >= MaxReferences;

        return new JatsArticleExtract
        {
            Format = "jats-extract",
            SourceFile = fileName,
            Identifiers = identifiers,
            Title = title,
            Journal = journal,
            Published = published,
            License = license,
            Authors = authors,
            Abstract = abstractText,
            Sections = sections,
            Tables = tables,
            References = references,
            ExtractionNotes = new JatsExtractionNotes
            {
                OriginalBytes = originalBytes,
                SectionsIncluded = sections.Count,
                ReferencesIncluded = references.Count,
                SectionsTruncated = sectionsTruncated,
                ReferencesTruncated = referencesTruncated,
                Guidance = "Use this pre-extracted structure for normalization. Do not request raw XML again."
            }
        };
    }

    private static Dictionary<string, string> ReadIdentifiers(XElement? articleMeta)
    {
        var identifiers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (articleMeta is null)
        {
            return identifiers;
        }

        foreach (XElement idElement in articleMeta.Elements().Where(e => e.Name.LocalName == "article-id"))
        {
            string? type = idElement.Attribute("pub-id-type")?.Value;
            string? value = idElement.Value?.Trim();
            if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            identifiers[type] = value;
        }

        return identifiers;
    }

    private static IReadOnlyList<string> ReadAuthors(XElement? articleMeta)
    {
        if (articleMeta is null)
        {
            return [];
        }

        var authors = new List<string>();
        foreach (XElement contrib in articleMeta.Descendants().Where(e => e.Name.LocalName == "contrib"))
        {
            XElement? name = FindFirst(contrib, "name");
            if (name is null)
            {
                continue;
            }

            string? surname = ReadText(FindFirst(name, "surname"));
            string? given = ReadText(FindFirst(name, "given-names"));
            string formatted = string.Join(
                ", ",
                new[] { surname, given }.Where(part => !string.IsNullOrWhiteSpace(part)));

            if (!string.IsNullOrWhiteSpace(formatted))
            {
                authors.Add(formatted);
            }

            if (authors.Count >= MaxAuthors)
            {
                break;
            }
        }

        return authors;
    }

    private static string ReadAbstract(XElement? articleMeta)
    {
        if (articleMeta is null)
        {
            return string.Empty;
        }

        XElement? abstractElement = FindFirst(articleMeta, "abstract");
        if (abstractElement is null)
        {
            return string.Empty;
        }

        var paragraphs = abstractElement
            .Descendants()
            .Where(e => e.Name.LocalName == "p")
            .Select(ReadText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        string abstractText = paragraphs.Count == 0
            ? ReadText(abstractElement)
            : string.Join("\n\n", paragraphs);

        return Truncate(NormalizeWhitespace(abstractText), MaxAbstractLength);
    }

    private static IReadOnlyList<JatsSectionExtract> ReadSections(XElement? body)
    {
        if (body is null)
        {
            return [];
        }

        var sections = new List<JatsSectionExtract>();
        foreach (XElement section in body.Descendants().Where(e => e.Name.LocalName == "sec"))
        {
            if (string.Equals(section.Attribute("sec-type")?.Value, "ref-list", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? sectionTitle = ReadText(FindFirst(section, "title"));
            if (string.IsNullOrWhiteSpace(sectionTitle))
            {
                continue;
            }

            var paragraphs = section
                .Elements()
                .Where(e => e.Name.LocalName == "p")
                .Select(ReadText)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();

            string sectionText = paragraphs.Count == 0
                ? string.Empty
                : string.Join("\n\n", paragraphs);

            sections.Add(new JatsSectionExtract
            {
                SectionId = section.Attribute("id")?.Value,
                Title = sectionTitle,
                Text = Truncate(NormalizeWhitespace(sectionText), MaxSectionTextLength)
            });

            if (sections.Count >= MaxSections)
            {
                break;
            }
        }

        return sections;
    }

    private static IReadOnlyList<JatsTableExtract> ReadTables(XElement? body)
    {
        if (body is null)
        {
            return [];
        }

        var tables = new List<JatsTableExtract>();
        foreach (XElement tableWrap in body.Descendants().Where(e => e.Name.LocalName == "table-wrap"))
        {
            XElement? table = FindFirst(tableWrap, "table");
            if (table is null)
            {
                continue;
            }

            var headers = table
                .Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "thead")
                ?.Descendants()
                .Where(e => e.Name.LocalName is "th" or "td")
                .Select(ReadText)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList() ?? [];

            var rows = new List<IReadOnlyList<string>>();
            foreach (XElement row in table.Descendants().Where(e => e.Name.LocalName == "tr"))
            {
                if (row.Ancestors().Any(a => a.Name.LocalName == "thead"))
                {
                    continue;
                }

                var cells = row
                    .Elements()
                    .Where(e => e.Name.LocalName is "td" or "th")
                    .Select(ReadText)
                    .ToList();

                if (cells.Count > 0)
                {
                    rows.Add(cells);
                }

                if (rows.Count >= MaxTableRows)
                {
                    break;
                }
            }

            tables.Add(new JatsTableExtract
            {
                Label = ReadText(FindFirst(tableWrap, "label")),
                Caption = ReadText(FindFirst(tableWrap, "caption")),
                Headers = headers,
                Rows = rows
            });

            if (tables.Count >= 5)
            {
                break;
            }
        }

        return tables;
    }

    private static IReadOnlyList<string> ReadReferences(XElement root)
    {
        var references = new List<string>();
        foreach (XElement refElement in root.Descendants().Where(e => e.Name.LocalName == "ref"))
        {
            XElement? citation = FindFirst(refElement, "mixed-citation")
                ?? FindFirst(refElement, "element-citation")
                ?? refElement;

            string citationText = NormalizeWhitespace(ReadText(citation));
            if (string.IsNullOrWhiteSpace(citationText))
            {
                continue;
            }

            references.Add(Truncate(citationText, 400));
            if (references.Count >= MaxReferences)
            {
                break;
            }
        }

        return references;
    }

    private static string? ReadPublicationDate(XElement? articleMeta)
    {
        XElement? pubDate = FindFirst(articleMeta, "pub-date");
        if (pubDate is null)
        {
            return null;
        }

        string? year = ReadText(FindFirst(pubDate, "year"));
        string? month = ReadText(FindFirst(pubDate, "month"));
        string? day = ReadText(FindFirst(pubDate, "day"));

        if (string.IsNullOrWhiteSpace(year))
        {
            return ReadText(pubDate);
        }

        if (string.IsNullOrWhiteSpace(month))
        {
            return year;
        }

        if (string.IsNullOrWhiteSpace(day))
        {
            return $"{year}-{month.PadLeft(2, '0')}";
        }

        return $"{year}-{month.PadLeft(2, '0')}-{day.PadLeft(2, '0')}";
    }

    private static XElement? FindFirst(XElement? parent, string localName) =>
        parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName)
        ?? parent?.Descendants().FirstOrDefault(e => e.Name.LocalName == localName);

    private static string ReadText(XElement? element)
    {
        if (element is null)
        {
            return string.Empty;
        }

        return NormalizeWhitespace(string.Concat(element.DescendantNodes().OfType<XText>().Select(node => node.Value)));
    }

    private static string NormalizeWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength
            ? value
            : string.Concat(value.AsSpan(0, maxLength), "…");

    private sealed class JatsArticleExtract
    {
        [JsonPropertyName("format")]
        public required string Format { get; init; }

        [JsonPropertyName("sourceFile")]
        public required string SourceFile { get; init; }

        [JsonPropertyName("identifiers")]
        public required IReadOnlyDictionary<string, string> Identifiers { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("journal")]
        public string? Journal { get; init; }

        [JsonPropertyName("published")]
        public string? Published { get; init; }

        [JsonPropertyName("license")]
        public string? License { get; init; }

        [JsonPropertyName("authors")]
        public required IReadOnlyList<string> Authors { get; init; }

        [JsonPropertyName("abstract")]
        public required string Abstract { get; init; }

        [JsonPropertyName("sections")]
        public required IReadOnlyList<JatsSectionExtract> Sections { get; init; }

        [JsonPropertyName("tables")]
        public required IReadOnlyList<JatsTableExtract> Tables { get; init; }

        [JsonPropertyName("references")]
        public required IReadOnlyList<string> References { get; init; }

        [JsonPropertyName("extractionNotes")]
        public required JatsExtractionNotes ExtractionNotes { get; init; }
    }

    private sealed class JatsSectionExtract
    {
        [JsonPropertyName("sectionId")]
        public string? SectionId { get; init; }

        [JsonPropertyName("title")]
        public required string Title { get; init; }

        [JsonPropertyName("text")]
        public required string Text { get; init; }
    }

    private sealed class JatsTableExtract
    {
        [JsonPropertyName("label")]
        public string? Label { get; init; }

        [JsonPropertyName("caption")]
        public string? Caption { get; init; }

        [JsonPropertyName("headers")]
        public required IReadOnlyList<string> Headers { get; init; }

        [JsonPropertyName("rows")]
        public required IReadOnlyList<IReadOnlyList<string>> Rows { get; init; }
    }

    private sealed class JatsExtractionNotes
    {
        [JsonPropertyName("originalBytes")]
        public required int OriginalBytes { get; init; }

        [JsonPropertyName("sectionsIncluded")]
        public required int SectionsIncluded { get; init; }

        [JsonPropertyName("referencesIncluded")]
        public required int ReferencesIncluded { get; init; }

        [JsonPropertyName("sectionsTruncated")]
        public required bool SectionsTruncated { get; init; }

        [JsonPropertyName("referencesTruncated")]
        public required bool ReferencesTruncated { get; init; }

        [JsonPropertyName("guidance")]
        public required string Guidance { get; init; }
    }
}
