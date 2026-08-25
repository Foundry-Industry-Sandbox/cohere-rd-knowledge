# Agent provisioning

## Purpose

Creates or updates the four Microsoft Foundry prompt agents used by the R&D Knowledge Mining workflow API.

In the standard Azure deployment flow, provisioning runs automatically as a Container Apps Job after infrastructure and MCP wiring complete. You do not need to run this CLI manually after clicking **Deploy to Azure**.

## Prompt agents included

| Agent | Model | Tools | Memory | Purpose |
|---|---|---|---|---|
| `ingestion-translation-agent` | Cohere Command A+ | `/raw-source/mcp` | Context across workflow steps | Read raw R&D knowledge; de-duplicate and normalize formats |
| `metadata-linking-agent` | Cohere Command A (dedicated deployment) | `/knowledge-search/mcp` | Context across workflow steps | Extract entities, link documents/datasets/studies, index to Vector DB |
| `search-chat-agent` | Cohere Command A+ | `/knowledge-search/mcp` | Session context | Grounded Q&A with citations and lineage |
| `curation-compliance-agent` | Cohere Command A+ | `/curation-compliance/mcp` | Session context | Review chat responses; flag gaps and sensitive content |

## Required environment variables

| Variable | Required | Description |
|---|---:|---|
| `AZURE_FOUNDRY_PROJECT_ENDPOINT` or `FOUNDRY_PROJECT_ENDPOINT` | Yes | Foundry project endpoint |
| `AZURE_AI_MODEL_DEPLOYMENT_NAME` or `ModelDeploymentName` | Yes | Model deployment name (default `cohere-command-a-plus`) |
| `AZURE_AI_METADATA_MODEL_DEPLOYMENT_NAME` | No | Dedicated metadata-linking deployment; falls back to `AZURE_AI_MODEL_DEPLOYMENT_NAME` when omitted |
| `MCP_BASE_URL` | Yes | Base URL of the MCP server (e.g. `https://<mcp-app>.azurecontainerapps.io`) |
| `AZURE_CLIENT_ID` | No (Azure) | Managed identity client ID |
| `AZURE_TOKEN_CREDENTIALS` | No (local) | Set to `AzureCliCredential` for a deterministic Azure CLI maintenance login |

Configuration file: `config/provisioning.json` (override locally with `config/provisioning.local.json`).

## Provisioning flow

During **Deploy to Azure**, [`infra/main.bicep`](../infra/main.bicep):

1. Provisions Foundry, Search, Storage, and model deployments.
2. Deploys the MCP Container App.
3. Starts the policy seed job.
4. Starts the agent provisioning Container Apps Job.
5. Waits for the job to finish before completing the deployment.

## How to create agents

For local or maintenance runs after MCP is reachable:

```powershell
$env:AZURE_TOKEN_CREDENTIALS = 'AzureCliCredential'
$env:AZURE_FOUNDRY_PROJECT_ENDPOINT = 'https://<foundry-resource>.services.ai.azure.com/api/projects/<project>'
$env:AZURE_AI_MODEL_DEPLOYMENT_NAME = 'cohere-command-a-plus'
$env:AZURE_AI_METADATA_MODEL_DEPLOYMENT_NAME = 'cohere-command-a'
$env:MCP_BASE_URL = 'https://<mcp-app>.azurecontainerapps.io'
dotnet restore agent-provisioning/src/CohereRndKnowledge.AgentProvisioning/CohereRndKnowledge.AgentProvisioning.csproj --configfile NuGet.config --source https://packagefeedproxy.microsoft.io/nuget/v3/index.json
dotnet run --no-restore --project agent-provisioning/src/CohereRndKnowledge.AgentProvisioning -- `
  --agents agent-provisioning/src/CohereRndKnowledge.AgentProvisioning/agents
```

## How to update agents

1. Edit agent assets under `src/CohereRndKnowledge.AgentProvisioning/agents/<agent-name>/`.
2. Re-run provisioning (Azure job on redeploy, or local command above).
3. The CLI compares the latest version's persisted asset fingerprint and creates a new version only when assets changed.

Versions created before asset fingerprints were persisted are migrated once on the next provisioning run. Subsequent unchanged runs do not create additional versions.

Agent-as-code layout per agent:

```text
agents/<agent-name>/
  agent.json
  instructions.md
  mcp.json
  governance.yaml
  rogue.yaml
```

Shared schemas live in `src/CohereRndKnowledge.AgentProvisioning/shared/`.

## How to validate provisioning

1. Confirm the provisioning job completed successfully in Azure Portal or deployment logs.
2. Open the Foundry project and verify all four agents exist.
3. Start an ingestion or query workflow from the frontend and confirm each agent stage completes.

## Troubleshooting

| Symptom | Possible cause | Resolution |
|---|---|---|
| Provisioning job fails | Invalid Foundry endpoint or model deployment | Verify `ProjectEndpoint` and `ModelDeploymentName` match deployed resources |
| Agent cannot call tools | MCP URL unreachable or wrong path | Set `MCP_BASE_URL` to the MCP Container App FQDN |
| `no_capacity` errors | Prompt plus reserved output exceeds available TPM, or deployment capacity is exhausted | Keep prompt and output budgets within deployed TPM; isolate the multi-turn metadata agent with `AZURE_AI_METADATA_MODEL_DEPLOYMENT_NAME`; increase capacity only when regional quota is available |

See also [`docs/agent-foundry-governance.md`](../docs/agent-foundry-governance.md) for governance policy layout.
