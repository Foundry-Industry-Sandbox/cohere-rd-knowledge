# Raw Layer (Corpus)

The canonical raw layer lives under `data-generation/corpus/`. It mixes real public sources and clearly marked synthetic operational records.

## Layout

```
corpus/
  source_catalog.json          scenario anchors and source list
  raw_manifest.json            file inventory with hashes
  xml/articles/pmc_oa/...      PMC OA JATS XML
  json/trials/...              ClinicalTrials.gov JSON
  json/datasets/geo/...        GEO metadata
  csv|txt/synthetic_eln_lims/  synthetic ELN/LIMS
  txt|md|html|pdf/agent_inputs/  optional multi-format evidence cards
```

## Scripts

| Script | Output |
|--------|--------|
| `generate_raw_layer.py` | Rebuilds `corpus/` from `source_catalog.json` (network) |
| `build_case_folders.py` | Slices/uploads into `dataset-seed/cases/*/ingest/` |
| `generate_agent_documents.py` | Optional evidence cards under `corpus/*/agent_inputs/` |
| `generate_normalized_layers.py` | Optional `ground-truth/` rollups |

## Regenerate

```bash
cd data-generation/scripts
python3 generate_raw_layer.py
python3 build_case_folders.py
```

Entities are derived in memory during ground-truth generation — there is no on-disk `entity-catalog/`.

## How to add a scenario

See [`../README.md`](../README.md#how-to-add-a-scenario).

## Privacy posture

- No patient-level data or patient names
- Synthetic people are fictional lab/reviewer names only
- GEO records are cell-line or assay-level where selected
