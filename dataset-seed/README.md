# Dataset seed

## Purpose

Demo-ready inputs for the HLS Agentic R&D Knowledge Mining workflow. Cases provide ingest payloads, query prompts, and policy text consumed by the backend in Local mode.

## Dataset contents

| Path | Description |
|---|---|
| `cases/case-04-demo/` | Headline demo (QRY-001 → ING-001 → QRY-002) |
| `cases/case-01-human-review/` … `case-09-multi-turn-query/` | Ingestion and query stress scenarios |
| `policies/hls_policies.txt` | HLS governance rules for policy RAG |
| `cases/catalog.json` | Scenario metadata (legacy IDs, outcomes, study info) |

PMC article files under `cases/*/ingest/` are compact JATS XML fixtures derived from full sources in `data-generation/corpus/`.

## File formats

| Format | Usage |
|---|---|
| `.xml` | PMC OA article fixtures |
| `.txt` | Exclusion cards, ELN/LIMS notes, query prompts |
| `.csv` | Synthetic LIMS manifests |
| `.json` | Scenario catalog |

## Schema

Scenario metadata follows fields in `cases/catalog.json`: `scenarioId`, `legacyScenarioId`, `caseFolder`, `sourceId`, `finalOutcome`, `study`, `sampleQuestion`.

Ground-truth rollups for validation: `data-generation/ground-truth/ING-*.json`, `QRY-*.json` (see `data-generation/ground-truth/SCHEMA.md`).

## Data relationships

- Ingestion cases (`ING-001` … `ING-007`) map to `cases/case-*/ingest/`.
- Query cases (`QRY-001` … `QRY-005`) map to `cases/case-*/prompts/`.
- Entity anchors (osimertinib, trials NCT02296125, PMC6889286, etc.) trace to `data-generation/corpus/` and [`../docs/source-baseline.md`](../docs/source-baseline.md).

## How the backend uses this data

- `Dataset:RootPath` points to this folder (`../../../dataset-seed` from Api.Host).
- Ingestion reads upload files from case folders via `sourceId` / `caseFolder`.
- Policies are seeded into `rd-policy-knowledge` index at deploy or MCP startup.
- Query prompts are sent from the frontend; the API does not auto-scan case folders for UI scenarios.

## Regenerating the dataset

From `data-generation/scripts/`:

```bash
python3 generate_raw_layer.py
python3 build_case_folders.py
```

See [`../data-generation/README.md`](../data-generation/README.md). After regeneration, update `PortfolioScenarios` in the frontend `appsettings.json`.

## Limitations

- Synthetic and public-source-derived data only — not for production or real patient use.
- UI scenario wiring is manual after adding new cases.
- Fabric mode uses OneLake `Files/bronze/cases/{caseId}/ingest/...` (same relative layout as local `cases/{caseId}/ingest`) when `DataSource:Mode` is `Fabric`.

## File reference

| File | Description | Used by |
|---|---|---|
| `policies/hls_policies.txt` | Trial, licensing, regional policy text | MCP policy seed, Curation & Compliance |
| `cases/catalog.json` | Scenario catalog metadata | Reference / tooling |
| `cases/case-04-demo/ingest/*.xml` | Five OA articles for headline ingest | ING-001 |
| `cases/case-04-demo/prompts/*.txt` | Empty-KB and grounded query prompts | QRY-001, QRY-002 |
| `cases/case-01-human-review/ingest/*` | Articles + exclusion cards | ING-002 |
| `cases/case-02-approval-labeling/ingest/*` | Synthetic ELN/LIMS files | ING-003 |
| `cases/case-03-sensitive-denied/ingest/*` | Patient-derived GEO exclusion | ING-004 |
| `cases/case-07-eu-policy-query/prompts/prompt.txt` | EU indication query | QRY-003 |
| `cases/case-09-multi-turn-query/prompts/*.txt` | Multi-turn chat prompts | QRY-005 |

Per-case README files under `cases/*/README.md` document expected outcomes.
