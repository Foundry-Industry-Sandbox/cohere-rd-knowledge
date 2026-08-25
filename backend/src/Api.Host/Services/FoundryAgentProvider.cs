using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Identity;
using CohereRndKnowledgeMining.Api.Host.Options;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;

namespace CohereRndKnowledgeMining.Api.Host.Services;

public sealed class FoundryAgentProvider
{
    private readonly AzureFoundryOptions _options;
    private readonly ILogger<FoundryAgentProvider> _logger;
    private readonly SemaphoreSlim _clientLoadLock = new(1, 1);
    private AIProjectClient? _projectClient;
    private AgentAdministrationClient? _agentClient;

    public FoundryAgentProvider(IOptions<AzureFoundryOptions> options, ILogger<FoundryAgentProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<AIAgent> GetIngestionTranslationAgentAsync(CancellationToken cancellationToken) =>
        LoadAgentAsync(_options.IngestionTranslationAgentName, cancellationToken);

    public Task<AIAgent> GetMetadataLinkingAgentAsync(CancellationToken cancellationToken) =>
        LoadAgentAsync(_options.MetadataLinkingAgentName, cancellationToken);

    public Task<AIAgent> GetSearchChatAgentAsync(CancellationToken cancellationToken) =>
        LoadAgentAsync(_options.SearchChatAgentName, cancellationToken);

    public Task<AIAgent> GetCurationComplianceAgentAsync(CancellationToken cancellationToken) =>
        LoadAgentAsync(_options.CurationComplianceAgentName, cancellationToken);

    private async Task<AIAgent> LoadAgentAsync(string agentName, CancellationToken cancellationToken)
    {
        (AIProjectClient projectClient, AgentAdministrationClient agentClient) =
            await GetClientsAsync(cancellationToken).ConfigureAwait(false);

        return await LoadPromptAgentAsync(projectClient, agentClient, agentName, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<(AIProjectClient ProjectClient, AgentAdministrationClient AgentClient)> GetClientsAsync(
        CancellationToken cancellationToken)
    {
        if (_projectClient is not null && _agentClient is not null)
        {
            return (_projectClient, _agentClient);
        }

        await _clientLoadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_projectClient is not null && _agentClient is not null)
            {
                return (_projectClient, _agentClient);
            }

            if (string.IsNullOrWhiteSpace(_options.ProjectEndpoint))
            {
                throw new InvalidOperationException(
                    "Azure Foundry configuration is missing. Set AzureFoundry:ProjectEndpoint in configuration or the AZURE_FOUNDRY_PROJECT_ENDPOINT environment variable.");
            }

            var credential = new DefaultAzureCredential();
            var projectEndpoint = new Uri(_options.ProjectEndpoint);
            _projectClient = new AIProjectClient(projectEndpoint, credential);
            _agentClient = new AgentAdministrationClient(projectEndpoint, credential);

            _logger.LogInformation(
                "Connected to Azure AI Foundry project at {Endpoint}",
                _options.ProjectEndpoint);

            return (_projectClient, _agentClient);
        }
        finally
        {
            _clientLoadLock.Release();
        }
    }

    private async Task<AIAgent> LoadPromptAgentAsync(
        AIProjectClient projectClient,
        AgentAdministrationClient agentClient,
        string agentName,
        CancellationToken cancellationToken)
    {
        try
        {
            ProjectsAgentRecord agentRecord = (await agentClient
                    .GetAgentAsync(agentName, cancellationToken)
                    .ConfigureAwait(false))
                .Value;

            AIAgent agent = projectClient.AsAIAgent(agentRecord);

            _logger.LogInformation(
                "Resolved Foundry prompt agent {AgentName} (record {AgentId}) as {AgentType}.",
                agentName,
                agentRecord.Id,
                agent.GetType().Name);

            return agent;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Required Foundry prompt agent '{agentName}' could not be resolved. Verify the agent exists in the project and that the caller is authenticated.",
                ex);
        }
    }
}
