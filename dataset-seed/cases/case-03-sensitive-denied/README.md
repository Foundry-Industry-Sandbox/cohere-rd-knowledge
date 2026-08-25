# Case 4 — Sensitive data denied

**User action:** Upload package containing patient-derived dataset candidate via ingestion console.
**Ingest:** 1 patient-derived GEO exclusion record — see flat file in `ingest/`.
**Expected outcome:** Curator gate returns `denied`; ingestion run closed. Indexed content removal deferred to a future iteration.
**Legacy ID:** ING-004

### User Input

Upload and ingest: experimental dataset package containing patient-derived GEO candidate (GSE301973) via ingestion console.
