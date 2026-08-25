# Data model

## Entities

Core domain entities for the osimertinib / EGFRm NSCLC demo:

| Entity | Examples | Source |
|---|---|---|
| Compound | Osimertinib (Tagrisso, AZD9291) | ChEMBL, FDA, EMA |
| Target | EGFR (CHEMBL203) | ChEMBL |
| Clinical trial | FLAURA (NCT02296125), AURA3, ADAURA, FLAURA2 | ClinicalTrials.gov |
| Article | PMC6889286, PMC5447962, … | PMC Open Access |
| Experimental dataset | GSE323366, GSE323365, … | NCBI GEO (cell-line only) |
| Policy | HLS-TRIAL-300, HLS-LIC-200, HLS-REGION-EU-400 | `hls_policies.txt` |
| Synthetic lab record | ELN/LIMS manifests | Derived from public GEO structure |

## Relationships

- Articles and trials link to compound, biomarkers (Ex19del, T790M, C797S), and study endpoints.
- Metadata & Linking agent extracts `linkedEntities` and `lineageNarrative` for vector index documents.
- Exclusion cards (`CUR-EXCLUDE-*`) represent datasets or articles blocked from ingestion.

## File schemas

**Scenario catalog** (`dataset-seed/cases/catalog.json`): array of scenario objects with `legacyScenarioId`, `caseFolder`, `finalOutcome`, `study`, `sampleQuestion`.

**Ground truth** (`data-generation/ground-truth/*.json`): stage-by-stage expected outcomes per `ING-*` / `QRY-*` scenario. Schema: `data-generation/ground-truth/SCHEMA.md`.

**Raw corpus** (`data-generation/corpus/`): JSON, XML, CSV, TXT organized by source type. Catalog: `source_catalog.json`.

## Example records

Trial anchor (from source baseline):

- NCT02296125 — FLAURA, Phase III, osimertinib vs erlotinib/gefitinib, 674 enrollment.

Article fixture (ingest):

- `PMC6889286_article.xml` — resistance mechanisms to osimertinib in EGFR-mutated NSCLC.

Policy reference:

- `HLS-REGION-EU-400` — EU regional transparency and data-handling expectations (used in QRY-003).

## How agents use the data

| Agent | Data use |
|---|---|
| Ingestion & Translation | Reads raw files from case ingest or Fabric; normalizes format |
| Metadata & Linking | Extracts entities, writes embedded chunks to `rd-knowledge-evidence` |
| Search & Chat | Retrieves Top-N passages; returns citations and lineage |
| Curation & Compliance | Reads chat responses; queries `rd-policy-knowledge`; flags sensitive content |

Full source baseline: [`source-baseline.md`](./source-baseline.md).
