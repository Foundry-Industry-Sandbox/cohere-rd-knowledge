# Configuration

Configuration precedence (highest wins): **command-line args → environment variables → optional local JSON overlays → `appsettings.*.json`**.

Environment variables always override values from `appsettings.json` and from `appsettings.Deployment.local.json` (MCP local overlay).

## Environment variables

### Api.Host

| Setting | Required | Description | Example |
|---|---:|---|---|
| `AZURE_FOUNDRY_PROJECT_ENDPOINT` | Yes (Azure) | Foundry project endpoint | `https://<account>.services.ai.azure.com/api/projects/<project>` |
| `AZURE_CLIENT_ID` | Yes (Azure) | User-assigned managed identity | `<guid>` |
| `AZURE_STORAGE_BLOB_SERVICE_URI` | Yes (Azure) | Blob service URI | `https://<account>.blob.core.windows.net` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | No | Telemetry | `InstrumentationKey=...` |
| `ASPNETCORE_ENVIRONMENT` | No | Runtime environment | `Production` |
| `ASPNETCORE_URLS` | No | Bind URL | `http://+:8080` |

### MCP server

| Setting | Required | Description | Example |
|---|---:|---|---|
| `AzureSearch:Endpoint` | Yes | AI Search endpoint | `https://<search>.search.windows.net` |
| `AzureFoundryModels:EmbedEndpoint` | Yes | Embed deployment endpoint | Foundry embed URL |
| `AzureFoundryModels:RerankEndpoint` | Yes | Rerank deployment endpoint | Foundry rerank URL |
| `Governance:EnableMcpToolGovernance` | No | Enable AGT runtime tool sandboxing on MCP | `true` |
| `Governance:AgentAuditStoreDirectory` | No | Governance audit JSONL directory | `data/agent-governance-audit` |
| `McpStartup__SeedPoliciesOnStartup` | No | Seed policies on start | `true` |
| `PolicySeed__PolicyFilePath` | No | Policies file path | Path to `hls_policies.txt` |

### Agent provisioning

| Setting | Required | Description | Example |
|---|---:|---|---|
| `AZURE_FOUNDRY_PROJECT_ENDPOINT` | Yes | Foundry project endpoint | Same as Api.Host |
| `AZURE_AI_MODEL_DEPLOYMENT_NAME` | Yes | Command model deployment | `cohere-command-a-plus` |
| `AZURE_AI_METADATA_MODEL_DEPLOYMENT_NAME` | No | Dedicated metadata-linking model deployment; defaults to the primary deployment when omitted | `cohere-command-a` |
| `MCP_BASE_URL` | Yes | MCP server base URL | `https://<mcp>.azurecontainerapps.io` |

### Frontend

| Setting | Required | Description | Example |
|---|---:|---|---|
| `ApiBaseUrl` | Yes | Api.Host base URL | `https://<api>.azurecontainerapps.io/` |

## Azure resources

Created by `infra/main.bicep`. Key outputs: `foundryProjectEndpoint`, `searchServiceEndpoint`, `apiUrl`, `frontendUrl`, `mcpUrl`.

## Model configuration

| Deployment | Default name | Purpose |
|---|---|---|
| Command A+ | `cohere-command-a-plus` | Ingestion, search/chat, and curation/compliance prompt agents |
| Command A | `cohere-command-a` | Metadata linking on a separate quota pool |
| embed-v4 | `cohere-embed-v4` | Embeddings |
| rerank-v4-pro | `cohere-rerank-v4-pro` | Reranking |

Adjust SKU and capacity via the primary `modelDeployment*` and dedicated `metadataModelDeployment*` parameters. Each capacity must not exceed the applicable regional model quota at deployment time.

## Agent configuration

`AzureFoundry` section in `Api.Host/appsettings.json`:

- `IngestionTranslationAgentName`
- `MetadataLinkingAgentName`
- `SearchChatAgentName`
- `CurationComplianceAgentName`

Must match names provisioned by `agent-provisioning`.

## Backend configuration

| Setting | Required | Description | Example |
|---|---:|---|---|
| `DataSource:Mode` | Yes | `Local` or `Fabric` | `Local` |
| `Dataset:RootPath` | Yes (Local) | Path to dataset-seed | `../../../dataset-seed` |
| `AzureSearch:KnowledgeIndexName` | Yes | Evidence index | `rd-knowledge-evidence` |
| `AzureSearch:PolicyIndexName` | Yes | Policy index | `rd-policy-knowledge` |
| `AzureStorage:ContainerName` | Yes | Document container | `rd-knowledge-documents` |

Fabric mode (demo; not production-ready): set `DataSource:FabricLakehouse` workspace and lakehouse names; enable Fabric in infra with `fabricIdentityName` (existing UAMI in the same resource group, default `fabric-identity`).

## Frontend configuration

`PortfolioScenarios` in `appsettings.json` — scenario catalog for the UI. Must align with `dataset-seed/cases/catalog.json` after dataset changes.

`WorkflowPolling` — status poll interval and timeout for long-running workflows. `TraceRefreshEveryNTicks` is reserved for future retrieval-trace refresh behavior.
