namespace RndKnowledgeMining.Mcp.Adapters;

public static class RawDocumentContentPreparer
{
    private const int MaxRawPreviewChars = 80_000;

    public static string PrepareForAgent(string fileName, string rawContent)
    {
        string trimmed = rawContent.Trim();

        if (JatsArticleExtractor.LooksLikeJatsArticle(fileName, trimmed))
        {
            return JatsArticleExtractor.ToAgentJson(fileName, trimmed);
        }

        if (trimmed.Length <= MaxRawPreviewChars)
        {
            return trimmed;
        }

        return string.Concat(
            trimmed.AsSpan(0, MaxRawPreviewChars),
            "\n\n[TRUNCATED: content exceeded MCP preview limit]");
    }
}
