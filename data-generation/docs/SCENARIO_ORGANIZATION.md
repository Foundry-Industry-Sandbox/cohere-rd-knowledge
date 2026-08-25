# Scenario Organization

Demo-first layout: each case is a self-contained folder under `dataset-seed/cases/`.

## Case folders

| Folder | Legacy ID | Role |
|--------|-----------|------|
| `case-01-human-review` | ING-002 | Guardrail review — human curator gate |
| `case-02-approval-labeling` | ING-003 | Synthetic ELN/LIMS with required labeling |
| `case-03-sensitive-denied` | ING-004 | Patient-derived candidate denied |
| `case-04-demo` | QRY-001, ING-001, QRY-002 | Stateful headline demo |
| `case-05-insufficient-data` | ING-005 | Empty/truncated batch — insufficient data |
| `case-06-approve-after-review` | ING-007 | Messy pool — curator approves with exclusions |
| `case-07-eu-policy-query` | QRY-003 | EU policy gap — compliance flag |
| `case-08-clarification-query` | QRY-004 | Ambiguous query — clarification needed |
| `case-09-multi-turn-query` | QRY-005 | Multi-turn grounded session + Curate |

## case-04-demo structure

```
case-04-demo/
  ingest/           ING-001 upload (5 OA articles)
  prompts/
    01-no-data-prompt.txt      QRY-001
    03-grounded-query-prompt.txt   QRY-002
  README.md
```

## Agent capability coverage

See [`TEST_CASES.md`](TEST_CASES.md#agent-capability-matrix) for the full matrix (covered vs gaps).

## Prerequisites

See [`TEST_CASES.md`](TEST_CASES.md#prerequisites-and-dependencies) before running query scenarios or the headline demo.

## Ground truth

Optional validation keys in `data-generation/ground-truth/<ID>.json` mirror the full e2e chain including memory stages (gates, persistence).

## Rebuild

```bash
cd data-generation/scripts
python3 generate_raw_layer.py
python3 build_case_folders.py
```

Scenario definitions: [`scenarios.py`](../scripts/scenarios.py)

## How to add a scenario

See [`../README.md`](../README.md#how-to-add-a-scenario).
