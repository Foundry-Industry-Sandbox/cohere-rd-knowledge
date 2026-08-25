# Case 5 — Insufficient data batch

**User action:** Upload batch via ingestion console (controlled UI trigger).
**Ingest:** Empty stub file and truncated protocol fragment — see `ingest/`.
**Expected outcome:** Ingestion translation returns `Insufficient Data`; metadata linking cannot proceed; curator denies persistence; nothing written to the Vector DB.
**Legacy ID:** ING-005

**Agent capabilities tested:** ingestion-translation `Insufficient Data`; metadata-linking `Insufficient Data`.
