# Case 6 — Approve after exclusion review

**User action:** Upload mixed research package via ingestion console (same pool as Case 1 / ING-002).
**Ingest:** 5 compact OA JATS XML articles plus 3 exclusion decision cards — see `ingest/` (`PMC*_article.xml`, `CUR-EXCLUDE-*.txt`).
**Expected outcome:** Ingestion flags exclusions (`CUR-EXCLUDE-PMC4771182`, GEO datasets excluded); curator **approves admitted content**; articles and accepted GEO datasets persisted.
**Legacy ID:** ING-007

**Agent capabilities tested:** same guardrail path as ING-002, but curator gate `approve_with_exclusions` and partial persistence.
