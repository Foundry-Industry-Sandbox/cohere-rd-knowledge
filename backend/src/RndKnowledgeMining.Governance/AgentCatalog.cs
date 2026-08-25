namespace RndKnowledgeMining.Governance;

public static class AgentCatalog
{
    public const string AgentRoleHeaderName = "X-Agent-Role";

    public const string IngestionTranslationAgentName = "ingestion-translation-agent";
    public const string MetadataLinkingAgentName = "metadata-linking-agent";
    public const string SearchChatAgentName = "search-chat-agent";
    public const string CurationComplianceAgentName = "curation-compliance-agent";

    public static string ToFolderName(AgentRole role) =>
        role switch
        {
            AgentRole.IngestionTranslation => IngestionTranslationAgentName,
            AgentRole.MetadataLinking => MetadataLinkingAgentName,
            AgentRole.SearchChat => SearchChatAgentName,
            AgentRole.CurationCompliance => CurationComplianceAgentName,
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };

    public static AgentRole FromFolderName(string folderName)
    {
        if (string.Equals(folderName, IngestionTranslationAgentName, StringComparison.OrdinalIgnoreCase))
        {
            return AgentRole.IngestionTranslation;
        }

        if (string.Equals(folderName, MetadataLinkingAgentName, StringComparison.OrdinalIgnoreCase))
        {
            return AgentRole.MetadataLinking;
        }

        if (string.Equals(folderName, SearchChatAgentName, StringComparison.OrdinalIgnoreCase))
        {
            return AgentRole.SearchChat;
        }

        if (string.Equals(folderName, CurationComplianceAgentName, StringComparison.OrdinalIgnoreCase))
        {
            return AgentRole.CurationCompliance;
        }

        throw new ArgumentException($"Unknown agent folder name '{folderName}'.", nameof(folderName));
    }

    public static IReadOnlyList<AgentRole> AllRoles { get; } =
    [
        AgentRole.IngestionTranslation,
        AgentRole.MetadataLinking,
        AgentRole.SearchChat,
        AgentRole.CurationCompliance
    ];

    public static bool TryResolveRoleFromHeader(string? headerValue, out AgentRole role)
    {
        role = default;
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return false;
        }

        try
        {
            role = FromFolderName(headerValue.Trim());
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static bool IsRoleAllowedOnPath(AgentRole role, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return role switch
        {
            AgentRole.IngestionTranslation => path.Contains("/raw-source/", StringComparison.OrdinalIgnoreCase),
            AgentRole.MetadataLinking => path.Contains("/knowledge-search/", StringComparison.OrdinalIgnoreCase),
            AgentRole.SearchChat => path.Contains("/knowledge-search/", StringComparison.OrdinalIgnoreCase),
            AgentRole.CurationCompliance => path.Contains("/curation-compliance/", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    public static bool IsMcpRequest(string? path)
    {
        string value = path ?? string.Empty;
        return value.Contains("/knowledge-search/", StringComparison.OrdinalIgnoreCase)
            || value.Contains("/curation-compliance/", StringComparison.OrdinalIgnoreCase)
            || value.Contains("/raw-source/", StringComparison.OrdinalIgnoreCase);
    }
}
