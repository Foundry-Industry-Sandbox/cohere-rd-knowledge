# Decision Ground Truth Schema

End-to-end ground truth for HLS's **two sequential phases** — one rollup per scenario:

- `ING-XXX.json` — PHASE 1 (ingestion & structuring)
- `QRY-XXX.json` — PHASE 2 (search & compliance)

Demo upload payloads live under `dataset-seed/cases/` (built by `build_case_folders.py`).
Ground-truth rollups are optional validation answer keys under `ground-truth/`.

## Scenario-level fields

- `scenario_id` (e.g. `ING-001`, `QRY-001`)
- `document_type` = `decision_ground_truth`
- `scenario_kind` = `e2e_phase_path`
- `flow` = `ingestion` | `search`,  `phase` = `1` | `2`
- `title`, `path`
- `scenario_folder` — demo case path under `dataset-seed/cases/`
- `trigger` — controlled UI action that starts the phase
- `kb_state` (search only) = `empty` | `populated`
- `stages`, `final_outcome`, `required_human_review`, `raw_sources`

## Per-stage fields (`stages[]`)

- `order`, `stage`, `kind` (`agent` | `output` | `gate` | `sink`)
- `materialized` — legacy flag; per-stage folders are no longer produced
- `raw_layer_folder` — always `null` (demo-first layout)
- primary payload keyed by kind: `agent_input` | `response` | `gate_record` | `persisted`
- `input_entities`, `output_entities`, `expected_output`, `decision`, `gate`
