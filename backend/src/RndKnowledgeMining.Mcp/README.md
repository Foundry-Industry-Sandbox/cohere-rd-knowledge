# R&D Knowledge Mining MCP Server

MCP tool provider for Block 2 agents (`search-chat-agent`, `curation-compliance-agent`). Requires **Azure AI Search** and **Microsoft Foundry** (Cohere embed + rerank) — there is no local fallback mode.

## MCP Endpoints

| MCP endpoint | Agents | Tools |
| --- | --- | --- |
| `/knowledge-search/mcp` | `search-chat-agent`, `metadata-linking-agent` | `search_rd_knowledge`, `get_knowledge_lineage`, `index_rd_knowledge`, `index_rd_knowledge_batch`, `list_normalized_documents`, `read_normalized_document` |
| `/curation-compliance/mcp` | `curation-compliance-agent` | `get_relevant_policies`, `get_policies_by_refs`, `flag_sensitive_content` |
| `/raw-source/mcp` | `ingestion-translation-agent` | `list_raw_documents`, `read_raw_document` |

Health check: `GET /health`

Default local URL: `http://localhost:5041`

## Governance

When `Governance:EnableMcpToolGovernance` is `true` (default), each MCP tool call is wrapped with AGT policy evaluation before the handler runs. Agent identity is resolved from the `X-Agent-Role` header and validated against the MCP route. Blocked calls return an MCP error; allowed and blocked events append to `data/agent-governance-audit/agent-governance-audit.jsonl`. See [docs/agent-foundry-governance.md](../../../docs/agent-foundry-governance.md).

## Data sources

| Index | Populated by |
| --- | --- |
| `rd-knowledge-evidence` | metadata-linking-agent via `index_rd_knowledge` MCP tool (before Knowledge Curator gate) |
| `rd-policy-knowledge` | Deploy-time or startup policy seed from `policies/hls_policies.txt` |

Knowledge documents indexed by Block 1 should include `linkedEntities` and `lineageNarrative` fields so `get_knowledge_lineage` can resolve traceability from Azure AI Search.

## Required configuration

Set in `appsettings.json`, `appsettings.Deployment.local.json`, or environment variables:

- `AzureSearch:Endpoint`
- `AzureFoundryModels:EmbedEndpoint`
- `AzureFoundryModels:RerankEndpoint`

Leave `AzureFoundryModels:ApiKey` empty in Azure to use managed identity (`https://ai.azure.com/.default`).

## Local development

```powershell
cd backend/src/RndKnowledgeMining.Mcp
# Configure appsettings.Deployment.local.json with your Azure endpoints
dotnet restore RndKnowledgeMining.Mcp.csproj --configfile ../../../NuGet.config --source https://packagefeedproxy.microsoft.io/nuget/v3/index.json
dotnet run --no-restore
```

Deploy-style policy seeding without starting the web host:

```powershell
dotnet restore RndKnowledgeMining.Mcp.csproj --configfile ../../../NuGet.config --source https://packagefeedproxy.microsoft.io/nuget/v3/index.json
dotnet run --no-restore -- --seed-policies
```

For optional Fabric setup, see [docs/fabric-setup.md](../../../docs/fabric-setup.md).
