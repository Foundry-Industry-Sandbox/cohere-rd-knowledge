# HLS Data Generation & Reference

Everything needed to **build, validate, and rebuild** the HLS dataset — not consumed directly at demo runtime.

## Layout

```
data-generation/
  corpus/                    canonical raw source files + source_catalog.json
  ground-truth/              optional e2e answer keys (ING-XXX.json, QRY-XXX.json)
  scripts/                   Python generators and build_case_folders.py
  docs/                      HANDOFF, TEST_CASES, RAW_LAYER, TESTING_GUIDE, etc.
```

Runtime demo package (generated output): [`../dataset-seed/`](../dataset-seed/)

## Regenerate demo cases

From `data-generation/scripts/`:

```bash
python3 generate_raw_layer.py       # corpus/ (needs network on first run)
python3 build_case_folders.py       # dataset-seed/cases/*/ingest/ + prompts/
```

Optional:

```bash
python3 generate_agent_documents.py        # pdf/txt/md/html agent_inputs in corpus/
python3 generate_normalized_layers.py      # ground-truth/ rollups (validation answer keys)
```

## How runtime discovers scenarios

`build_case_folders.py` writes `ingest/` and `prompts/` under `dataset-seed/cases/`. The UI does not scan those folders automatically — each scenario must be registered in frontend config.

| Layer | Behavior |
|-------|----------|
| **UI** | `PortfolioScenarioService` reads `frontend/src/WebApp/appsettings.json` (`PortfolioScenarios`). Add an entry per scenario: `scenarioId`, `caseFolder`, `sourceId`, title, description, study metadata, ingestion vs query block. |
| **API** | Backend does not gate case ids; wiring is a frontend config step after regenerating the dataset package. |
| **Case folders** | Semantic names (`case-01-human-review`, `case-04-demo`, …), mapped in `CASE_FOLDERS` and `DEMO_PROMPT_FILES` in `scenarios.py`. |

There is no `catalog.json` for the UI in this repo. Ingestion and query are separate UI flows.

## Legacy scenario IDs

Scripts and ground truth use trackable prefixes: `ING-XXX` (ingestion phase), `QRY-XXX` (search phase). Demo-facing folder names (`case-01-human-review`, `case-04-demo`, etc.) are mapped in `scripts/scenarios.py`.

## How to add a scenario

New scenarios are written into `dataset-seed/`. They do **not** appear in a running app until you rebuild the generated assets, republish container images or deployment packages, and redeploy.

### 1. Plan the scenario

- **Ingestion** (`ING-XXX`) — upload payload, curator gate, persistence outcome (`approved_persisted`, `needs_human_review`, `denied_not_persisted`, etc.).
- **Search** (`QRY-XXX`) — query text, KB state (`empty` vs `populated`), grounded vs no-grounded answer.
- Reuse entities from the osimertinib / EGFR corpus where possible (`source_catalog.json`, existing `RDOC-*`, `TRIAL-*`, `DATASET-*`).

### 2. Add or update corpus sources

| Need | Action |
|------|--------|
| Reuse existing articles, trials, GEO datasets | Reference entity ids in `scenarios.py` only |
| New public source (PMC, trial, GEO) | Add to `corpus/source_catalog.json`, run `generate_raw_layer.py` (network required) |
| New exclusion candidate | Add to `excluded_sources[]` in `source_catalog.json` — generates `CUR-EXCLUDE-*` entities |
| Synthetic ELN/LIMS | Extend `build_synthetic_eln_lims()` in `generate_raw_layer.py` |

### 3. Declare the scenario

Add an entry to `INGESTION_SCENARIOS` or `SEARCH_SCENARIOS` in `scripts/scenarios.py`:

- `scenario_id`, `flow`, `phase`, `path`, `title`, `final_outcome`, `trigger`
- `stages[]` with `kind`: `agent` | `output` | `gate` | `sink`
- For ingestion: `input_entities_raw` or `input_raw` on the `ingestion_translation` stage
- For search: `prompt` text and expected citations on search stages

Map demo folders:

- Ingestion → `CASE_FOLDERS` (`"ING-XXX": "case-NN-name"`)
- Headline demo queries → `DEMO_PROMPT_FILES` under `case-04-demo/prompts/`

### 4. Regenerate derived assets

```bash
cd data-generation/scripts
python3 generate_raw_layer.py      # if corpus or catalog changed
python3 build_case_folders.py
python3 generate_normalized_layers.py   # if ground-truth rollups should change
```

Review the diff under `dataset-seed/` carefully before committing.

### 5. Wire the UI (required)

See [How runtime discovers scenarios](#how-runtime-discovers-scenarios). Add a `PortfolioScenarios` entry in `frontend/src/WebApp/appsettings.json`:

- `scenarioId`, `legacyScenarioId`, `caseFolder`, `sourceId`, `title`, `description`, `finalOutcome`, `outcomeHint`
- For ingestion scenarios: `studyId`, `sourceId`, and `caseFolder` must match the folder produced by `CASE_FOLDERS` in `scenarios.py`
- For headline demo queries: ensure `DEMO_PROMPT_FILES` in `scenarios.py` matches the prompt file under `case-04-demo/prompts/` if applicable

### 6. Review output

Check before committing:

- `dataset-seed/cases/{caseFolder}/ingest/` (and `prompts/` for query scenarios)
- `ground-truth/ING-XXX.json` or `ground-truth/QRY-XXX.json`
- Case `README.md` (preserved by the build script if it already exists; write manually for new folders)

### 7. Rebuild and redeploy

Rebuild any image or deployment package that embeds `dataset-seed/`, then redeploy.

### Runtime notes (HLS-specific)

- Human-approval gates and persistence are **memory stages** in ground-truth rollups; only upload payloads and query prompts are materialized on disk.
- The domain is anchored to osimertinib / EGFRm NSCLC; a different therapeutic area requires a broad corpus and scenario rewrite.

## Key docs

- [`docs/HANDOFF.md`](docs/HANDOFF.md) — agent handoff map
- [`docs/TEST_CASES.md`](docs/TEST_CASES.md) — scenario index, prerequisites, [agent capability matrix](docs/TEST_CASES.md#agent-capability-matrix)
- [`docs/SCENARIO_ORGANIZATION.md`](docs/SCENARIO_ORGANIZATION.md) — case ↔ legacy id map
- [`docs/TESTING_GUIDE.md`](docs/TESTING_GUIDE.md) — e2e demo runbook
- [`ground-truth/SCHEMA.md`](ground-truth/SCHEMA.md) — rollup schema
