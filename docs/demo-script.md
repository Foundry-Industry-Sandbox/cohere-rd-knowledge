# Demo script

## Demo goal

Show how multi-agent ingestion, RAG-backed search, compliance curation, and human oversight work together on HLS scientific evidence using Microsoft Foundry and Cohere — without claiming production readiness.

## Setup

1. Deploy the sample (or run locally with Azure-backed Search and Foundry).
2. Open the frontend URL (`frontendUrl` output).
3. Confirm the vector index is empty for the headline opening (use **Reset demo** on the portfolio page if a prior run populated the KB).
4. Allow 15–20 minutes for the full narrative.

## Step-by-step flow

### Headline demo (`case-04-demo`)

| Step | Action | What to highlight |
|---|---|---|
| 1 — QRY-001 | Run the osimertinib resistance query on an **empty** KB | Honest “no grounded answer” behavior |
| 2 — ING-001 | Upload five OA articles; start ingestion | Ingestion & Translation → Metadata & Linking → indexing |
| 3 — HITL (Block 1) | Approve at Knowledge Curator gate | Human oversight after indexing |
| 4 — QRY-002 | Repeat the same query | Grounded answer with citations and lineage |
| 5 — Curate | Click **Curate** after one or more chat turns | Curation & Compliance over session responses |
| 6 — HITL (Block 2) | Approve or deny curation outcome | Compliance Reviewer gate |

### Optional stress scenarios

- **ING-002** — messy pool routed to curator (exclusion cards).
- **ING-004** — sensitive patient-derived candidate denied.
- **QRY-003** — EU policy gap flagged after grounded answer.

## Expected outputs

- Ingestion **workflow timeline** showing agent step state (Ingestion & Translation → Metadata & Linking → Knowledge Curator).
- Ingestion agent outputs and MCP index calls in the workspace panels.
- Query **workflow timeline** for Search & Chat and Curate stages.
- Search responses with `citations` and decision (`Answered`, `Insufficient Evidence`, `Clarification Needed`).
- **RAG retrieval trace** panels in the ingestion workspace and chat transcript when the API returns `retrievalTrace` events.
- Curation output with `flags`, `policyRefs`, and `required_human_review` when applicable.

## Resetting the demo

1. On the portfolio page, click **Reset demo** and confirm. This calls:
   - `POST /api/rd-knowledge/vector-db/clear-data` — clears the `rd-knowledge-evidence` index
   - `POST /api/rd-knowledge/query/sessions/reset` — clears in-memory query chat sessions and curate executions
2. The frontend also clears its local session cache and browser session state, then reloads the portfolio.
3. Re-run ING-001 before query scenarios that require a populated KB.

This does not delete normalized blobs in storage, the policy index, or MCP governance audit logs.

See also [troubleshooting.md](./troubleshooting.md).
