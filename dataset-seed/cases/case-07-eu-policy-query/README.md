# Case 7 — EU policy gap query

**Prerequisite:** Run **ING-001** first so the Vector DB is populated with osimertinib evidence.

**User action:** Run EU-scoped query against a populated KB.
**Prompt:** `prompts/prompt.txt`
**Expected outcome:** Search & Chat returns a grounded answer; Curate flags `missing_eu_regional_policy_reference` (`HLS-REGION-EU-400`); Compliance Reviewer approves with flags.
**Legacy ID:** QRY-003

**Agent capabilities tested:** search-chat grounded answer; curation-compliance `Flag for Review` for missing EU regional context.
