namespace RndKnowledgeMining.Governance;

public static class AgentIdentityCatalog
{
    public const string PolicyBundleVersion = "v1";

    public static string ToMeshAgentId(AgentRole role) =>
        role switch
        {
            AgentRole.IngestionTranslation => "did:mesh:rd-ingestion-translation",
            AgentRole.MetadataLinking => "did:mesh:rd-metadata-linking",
            AgentRole.SearchChat => "did:mesh:rd-search-chat",
            AgentRole.CurationCompliance => "did:mesh:rd-curation-compliance",
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };
}
