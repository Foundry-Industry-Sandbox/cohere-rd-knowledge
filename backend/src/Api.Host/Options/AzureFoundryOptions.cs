namespace CohereRndKnowledgeMining.Api.Host.Options;

public sealed class AzureFoundryOptions
{
    public const string SectionName = "AzureFoundry";

    public string ProjectEndpoint { get; set; } = string.Empty;

    public string IngestionTranslationAgentName { get; set; } = "ingestion-translation-agent";

    public string MetadataLinkingAgentName { get; set; } = "metadata-linking-agent";

    public string SearchChatAgentName { get; set; } = "search-chat-agent";

    public string CurationComplianceAgentName { get; set; } = "curation-compliance-agent";
}
