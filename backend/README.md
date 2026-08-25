# Backend

## Purpose

ASP.NET Core API (`Api.Host`) that orchestrates the R&D Knowledge Mining workflow with Agent Framework, calls Microsoft Foundry prompt agents, and exposes REST endpoints for the Blazor frontend.

## Architecture

- **Api.Host** — REST API, workflow orchestration, HITL gates.
- **RndKnowledgeMining.Mcp** — MCP server exposing knowledge search, curation/compliance, and raw-source tools.
- **dataset-seed** — local demo inputs consumed when `DataSource:Mode` is `Local`.

## Main workflow

Two independent blocks (see [`../workflow-summary.md`](../workflow-summary.md)):

**Block 1 — Ingestion:** `ingestion-translation-agent` → `metadata-linking-agent` → Knowledge Curator gate.

**Block 2 — Querying:** Search & Chat (`search-chat-agent`) as an interactive loop; **Curate** (`curation-compliance-agent`) on user action → Compliance Reviewer gate.

Key files:

- `src/Api.Host/Workflow/IngestionWorkflowFactory.cs`
- `src/Api.Host/Workflow/QueryWorkflowFactory.cs`
- `src/Api.Host/Services/IngestionWorkflowService.cs`
- `src/Api.Host/Services/QueryWorkflowService.cs`

## Agent Framework orchestration

Workflows are built with Agent Framework. Each step invokes a Foundry prompt agent; structured JSON output is parsed for UI display and HITL decisions. Human approval pauses are resumed via dedicated resume endpoints. Metadata linking includes retry handling for transient Foundry errors.

## MCP servers

Implemented in `src/RndKnowledgeMining.Mcp/`. See that folder's README for tool lists and configuration.

| MCP path | Used by |
|---|---|
| `/raw-source/mcp` | Ingestion & Translation |
| `/knowledge-search/mcp` | Metadata & Linking, Search & Chat |
| `/curation-compliance/mcp` | Curation & Compliance |

## REST API

Base URL (local): `http://localhost:8080`

## API endpoints

| Method | Path | Description |
|---|---|---|
| `GET` | `/health` | Health check |
| `GET` | `/api/rd-knowledge/scenarios` | Portfolio scenario catalog |
| `GET` | `/api/rd-knowledge/cases/{caseId}/documents` | Raw knowledge sources for a case |
| `GET` | `/api/rd-knowledge/vector-db/summary` | Vector DB index summary |
| `POST` | `/api/rd-knowledge/vector-db/clear-data` | Clear `rd-knowledge-evidence` index |
| `POST` | `/api/rd-knowledge/ingestion/start` | Start Block 1 ingestion workflow |
| `GET` | `/api/rd-knowledge/ingestion/executions/{executionId}/status` | Ingestion workflow status |
| `POST` | `/api/rd-knowledge/ingestion/sources/{sourceId}/executions/{executionId}/resume` | Resume after Knowledge Curator gate |
| `POST` | `/api/rd-knowledge/query/ask` | Search & Chat (Block 2) |
| `POST` | `/api/rd-knowledge/query/curate/start` | Start curation workflow |
| `GET` | `/api/rd-knowledge/query/curate/executions/{executionId}/status` | Curation workflow status |
| `POST` | `/api/rd-knowledge/query/curate/sessions/{sessionId}/executions/{executionId}/resume` | Resume after Compliance Reviewer gate |
| `POST` | `/api/rd-knowledge/query/sessions/reset` | Clear in-memory query chat sessions and curate executions |

## Configuration

Primary settings in `src/Api.Host/appsettings.json` and environment variables. See [`../docs/configuration.md`](../docs/configuration.md).

Key sections: `AzureFoundry`, `AzureSearch`, `AzureFoundryModels`, `AzureStorage`, `DataSource`, `Dataset`.

## Local run

**Terminal 1 — MCP server** (requires Azure Search + Foundry endpoints):

```powershell
cd backend/src/RndKnowledgeMining.Mcp
# Configure appsettings.Deployment.local.json
dotnet restore RndKnowledgeMining.Mcp.csproj --configfile ../../../NuGet.config --source https://packagefeedproxy.microsoft.io/nuget/v3/index.json
dotnet run --no-restore
```

**Terminal 2 — API:**

```powershell
cd backend/src/Api.Host
dotnet restore CohereRndKnowledgeMining.Api.Host.csproj --configfile ../../../NuGet.config --source https://packagefeedproxy.microsoft.io/nuget/v3/index.json
dotnet run --no-restore --urls http://localhost:8080
```

Provision agents first — see [`../agent-provisioning/README.md`](../agent-provisioning/README.md).

## Tests

No automated test project is included in this sample. Validate manually via the frontend or API calls against configured Azure resources.

## Observability

- Application Insights when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set (Azure deployment).
- Workflow status endpoints return per-agent JSON outputs for UI inspection.
- `WorkflowTokenDiagnostics` logs large payloads and agent-turn details to API logs during ingestion.
- MCP governance audit JSONL at `data/agent-governance-audit` on the MCP host.
- The frontend renders workflow timelines and RAG retrieval trace panels when `retrievalTrace` is present in API responses.

## Troubleshooting

| Symptom | Possible cause | Resolution |
|---|---|---|
| Ingestion fails immediately | Missing Foundry endpoint or agent names | Verify `AzureFoundry:ProjectEndpoint` and provisioned agent names |
| Empty search results | Knowledge index empty or wrong endpoint | Run ING-001 demo case; check `AzureSearch:Endpoint` and index name |
| MCP connection errors | MCP not running or wrong URL | Start MCP locally; set MCP base URL in agent provisioning |

See [`docs/troubleshooting.md`](../docs/troubleshooting.md). MCP details: [`src/RndKnowledgeMining.Mcp/README.md`](src/RndKnowledgeMining.Mcp/README.md). Dataset: [`dataset-seed/README.md`](../dataset-seed/README.md).
