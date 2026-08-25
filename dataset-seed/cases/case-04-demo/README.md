# Demo flow — stateful headline demo

Run these steps **in order** to show the full HLS narrative: query empty KB → ingest → query again and get grounded answer.

| Step | Legacy ID | Action | Location | Prerequisite |
|------|-----------|--------|----------|--------------|
| 1 | QRY-001 | Query empty KB | `prompts/01-no-data-prompt.txt` | KB must be **empty** |
| 2 | ING-001 | Upload & ingest clean OA articles | `ingest/` | — |
| 3 | QRY-002 | Same query — grounded answer with citations | `prompts/03-grounded-query-prompt.txt` | Step 2 complete (KB populated) |

See [`../../../data-generation/docs/TEST_CASES.md`](../../../data-generation/docs/TEST_CASES.md#prerequisites-and-dependencies) for all scenario dependencies.
