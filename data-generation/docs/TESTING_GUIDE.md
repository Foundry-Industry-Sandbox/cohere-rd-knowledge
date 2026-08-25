# Testing Guide

Run demos using files under `dataset-seed/cases/`. No per-stage folder tree is required.

## Prerequisites

Before picking a case, check KB state and ordering:

| Scenario type | Requirement |
|---------------|-------------|
| **QRY-001** | Vector DB must be **empty** |
| **QRY-002, QRY-003, QRY-005** | Vector DB must be **populated** — run **ING-001** first |
| **QRY-004** | Populated KB recommended (run ING-001 first) |
| **ING-001 … ING-007** | No prior scenario; each case has its own `ingest/` payload |
| **Headline demo** | Fixed order: QRY-001 → ING-001 → QRY-002 |

Full table: [`TEST_CASES.md`](TEST_CASES.md#prerequisites-and-dependencies).

## Quick start

1. Load `dataset-seed/policies/hls_policies.txt` into your RAG pipeline.
2. Pick a case from [`dataset-seed/README.md`](../../dataset-seed/README.md).
3. Upload flat files from `<case>/ingest/` when the case requires ingest.
4. For the headline demo (`case-04-demo`), run steps 1→3 in order using `prompts/` and `ingest/`.

## Headline demo (case-04-demo)

| Step | Legacy ID | Action | Files |
|------|-----------|--------|-------|
| 1 | QRY-001 | Query empty KB | `prompts/01-no-data-prompt.txt` |
| 2 | ING-001 | Upload 5 OA articles | `ingest/PMC*_article.xml` |
| 3 | QRY-002 | Same query, grounded answer | `prompts/03-grounded-query-prompt.txt` |

Expected outcomes are documented in each case README and in `ground-truth/<ID>.json`.

## Standalone stress cases

| Case folder | Legacy ID | Ingest | Prerequisite |
|-------------|-----------|--------|--------------|
| `case-01-human-review` | ING-002 | 5 × `PMC*_article.xml` + 3 × `CUR-EXCLUDE-*.txt` | None |
| `case-02-approval-labeling` | ING-003 | ELN/LIMS csv + txt | None |
| `case-03-sensitive-denied` | ING-004 | `CUR-EXCLUDE-GSE301973.txt` | None |
| `case-05-insufficient-data` | ING-005 | empty + truncated txt | None |
| `case-06-approve-after-review` | ING-007 | Same 5 articles + 3 exclusion cards as ING-002 | None (not a follow-up to ING-002) |
| `case-07-eu-policy-query` | QRY-003 | — (query only) | **ING-001** (populated KB) |
| `case-08-clarification-query` | QRY-004 | — (query only) | ING-001 recommended |
| `case-09-multi-turn-query` | QRY-005 | — (2 prompts, same session) | **ING-001** (populated KB) |

## Rebuild test data

```bash
cd data-generation/scripts
python3 generate_raw_layer.py
python3 build_case_folders.py
```

Optional: `python3 generate_agent_documents.py` before `build_case_folders.py` if ING-004 agent-input txt must be refreshed.

## How to add a scenario

See [`../README.md`](../README.md#how-to-add-a-scenario).
