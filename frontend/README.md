# Frontend

## Purpose

Blazor Server web app for running ingestion and query workflows, inspecting agent traces, and completing human-in-the-loop approve/deny steps.

## User experience

- **Portfolio** — select ingestion or query demo scenarios; **Reset demo** clears the knowledge index and query sessions for headline re-runs.
- **Ingestion console** — upload case files, start workflow, review agent steps and workflow timeline, approve/deny at the Knowledge Curator gate.
- **Query workspace** — ask questions (Search & Chat), view citations and retrieval trace panels, start **Curate**, approve/deny at the Compliance Reviewer gate.

## Configuration

`src/WebApp/appsettings.json` and `appsettings.Development.json`:

| Key | Purpose |
|---|---|
| `ApiBaseUrl` | Base URL of Api.Host (include trailing slash) |
| `PortfolioScenarios:Scenarios` | Scenario catalog for the portfolio page |
| `WorkflowPolling:IntervalSeconds` | Status poll interval |
| `WorkflowPolling:MaxDurationMinutes` | Max poll duration |
| `WorkflowPolling:TraceRefreshEveryNTicks` | Reserved for retrieval-trace refresh (not yet used by polling loops) |

Scenario entries must align with `dataset-seed/cases/` and `data-generation/scripts/scenarios.py`.

## Local run

```powershell
# Start Api.Host first (see ../backend/README.md)
cd frontend/src/WebApp
dotnet restore Cohere.AgenticRDKnowledge.WebApp.csproj --configfile ../../../NuGet.config --source https://packagefeedproxy.microsoft.io/nuget/v3/index.json
dotnet run --no-restore
```

Open `http://localhost:5147` (default from `launchSettings.json`).

## Build

```powershell
cd frontend/src/WebApp
dotnet restore Cohere.AgenticRDKnowledge.WebApp.csproj --configfile ../../../NuGet.config --source https://packagefeedproxy.microsoft.io/nuget/v3/index.json
dotnet build --no-restore
dotnet publish -c Release -o ./publish --no-restore
```

## Environment variables

| Variable | Purpose |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Development` or `Production` |
| `ASPNETCORE_URLS` | Bind URL (`http://+:8080` in Container Apps) |
| `ApiBaseUrl` | Injected in Azure to the API Container App FQDN |

## Connecting to backend

Set `ApiBaseUrl` to the running Api.Host URL (local: `http://localhost:8080/`).

## Troubleshooting

| Symptom | Possible cause | Resolution |
|---|---|---|
| Portfolio empty or wrong scenarios | `PortfolioScenarios` out of sync | Update `appsettings.json` after regenerating dataset |
| API errors in UI | `ApiBaseUrl` mismatch | Match Api.Host URL in `appsettings.Development.json` |
| Polling timeout | Workflow exceeds `MaxDurationMinutes` | Increase polling settings or check backend logs |

This frontend is .NET Blazor Server — no Node.js or `package.json` is required.
