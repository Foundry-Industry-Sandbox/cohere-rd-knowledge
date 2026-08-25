#!/usr/bin/env python3
"""
Scenario definitions for the HLS Agentic R&D knowledge mining dataset.

Single source of truth for the test cases. The workflow (see ../workflow-summary.md and the
architecture diagram) is **two sequential phases**, each closed by a distinct human actor:

  PHASE 1 — Ingestion & structuring:  ingestion & translation -> metadata & linking
                                      -> [Knowledge curator approves] -> persistence to CMS/KB
  PHASE 2 — Search & compliance:      search & chat -> curation & compliance
                                      -> [Compliance owner approves] -> response

Phase 2 can run immediately after phase 1's approval or be deferred (a researcher queries the
hub days later, or a compliance audit is scheduled). The orchestrator persists phase-1 context
and resumes phase 2 without re-running ingestion. The headline demo shows this statefully:
query an EMPTY KB (no answer) -> ingest -> query the POPULATED KB (grounded answer).

## What we materialize (and what we don't)

The demo *traverses every agent and both human actors*, but only **demo upload payloads** are
materialized under `dataset-seed/cases/` — exactly the inesite pattern:

  - Ingestion scenarios -> flat files in `<case>/ingest/` (built by `build_case_folders.py`).
  - Search scenarios in the headline demo -> query text in `case-04-demo/prompts/`.
  - Human-approval gates and persistence are **memory stages** in ground-truth rollups only.

Generators import this module so rollups and demo folders stay aligned:

  - generate_normalized_layers.py  -> ground-truth/{ING,QRY}-XXX.json
  - build_case_folders.py          -> dataset-seed/cases/*/ingest/ + prompts/

Trackable prefixes: ING-### (ingestion phase) and QRY-### (query/search phase), like APP-XXX in loan.

## Stage shapes

  - kind == "agent"   : a data-consuming agent. Materialized. Fields: agent, agent_input,
                        input_entities | input_raw | input_entities_raw | input_from_output_raw,
                        output_entities, expected_output, decision, gate.
  - kind == "output"  : the final response. Materialized (output-only). Field: response.
  - kind == "gate"    : a human-in-the-loop approval. Memory only. Fields: actor, gate_record,
                        decision, gate.
  - kind == "sink"    : persistence into the CMS/KB. Memory only. Field: persisted.
"""

from __future__ import annotations

# Stage kinds that historically had per-stage folders (now demo-first; kept for ground-truth metadata).
MATERIALIZED_KINDS = {"agent", "output"}

# kind -> (primary filename, payload key on the stage dict). Memory kinds (gate/sink) keep their
# payload in scenario.json only.
STAGE_PRIMARY = {
    "agent": ("agent_input.json", "agent_input"),
    "output": ("response.json", "response"),
    "gate": (None, "gate_record"),
    "sink": (None, "persisted"),
}

# The DEMO_SCENARIO bundle (stateful headline demo) and the standalone guardrail variants.
DEMO_SEQUENCE = ["QRY-001", "ING-001", "QRY-002"]
STANDALONE_SCENARIOS = [
    "ING-002", "ING-003", "ING-004", "ING-005", "ING-007",
    "QRY-003", "QRY-004", "QRY-005",
]

DEMO_CASE = "case-04-demo"
CASE_FOLDERS: dict[str, str] = {
    "ING-002": "case-01-human-review",
    "ING-003": "case-02-approval-labeling",
    "ING-004": "case-03-sensitive-denied",
    "ING-005": "case-05-insufficient-data",
    "ING-007": "case-06-approve-after-review",
    "QRY-003": "case-07-eu-policy-query",
    "QRY-004": "case-08-clarification-query",
    "QRY-005": "case-09-multi-turn-query",
}
DEMO_PROMPT_FILES: dict[str, str] = {
    "QRY-001": "01-no-data-prompt.txt",
    "QRY-002": "03-grounded-query-prompt.txt",
}
CASE_QUERY_PROMPTS: dict[str, list[str]] = {
    "QRY-003": ["prompt.txt"],
    "QRY-004": ["prompt.txt"],
    "QRY-005": ["01-resistance-mechanisms.txt", "02-flaura-context.txt"],
}

PORTAL_MANIFEST = "corpus/csv/partner_vendor_repositories/partner_vendor_repository_index.csv"
SYNTHETIC_RAW = [
    "corpus/csv/synthetic_eln_lims/lims_sample_manifest.csv",
    "corpus/txt/synthetic_eln_lims/eln_experiment_notebook.txt",
    "corpus/txt/synthetic_eln_lims/lims_quality_control_report.txt",
]

# Controlled UI triggers (button/process), NOT free-form chatbots.
UI_UPLOAD = {
    "type": "ui_action",
    "action": "upload_and_ingest",
    "surface": "ingestion_console",
    "note": "controlled button/process trigger — not a free-form chatbot",
    "external_connector_conceptual": "Connect Portals — uploaded files stand in for data that "
                                     "could come from external medical portals",
}
UI_QUERY = {
    "type": "ui_action",
    "action": "run_query",
    "surface": "search_console",
    "note": "controlled preset query — not a free-form chatbot",
}

CURATOR = "Marisol Vega, Knowledge Curator (fictional)"
COMPLIANCE_OWNER = "Daniel Okafor, Compliance Owner (fictional)"

_GROUNDED_QUERY = (
    "What are the known mechanisms of resistance to osimertinib in EGFR-mutated NSCLC, "
    "and which clinical-trial and regulatory evidence support its first-line use?"
)
_ANSWER_POINTS = [
    "osimertinib is a third-generation EGFR-TKI active against the EGFR T790M resistance mutation",
    "acquired resistance is heterogeneous: MET/HER2 amplification, RAS-MAPK or RAS-PI3K pathway "
    "activation, novel fusion events, and histological/phenotypic transformation",
    "FLAURA (NCT02296125) is the first-line trial context for osimertinib",
    "the Tagrisso openFDA label is the regulatory anchor for the regimen",
]
_GROUNDED_CITATIONS = ["RDOC-PMC6889286", "TRIAL-NCT02296125", "LBL-TAGRISSO-OPENFDA"]
_RETRIEVAL_SCOPE = ["RDOC-PMC6889286", "TRIAL-NCT02296125", "DATASET-GSE323366", "LBL-TAGRISSO-OPENFDA"]

_EU_QUERY = (
    "What is the EU/EMA approved first-line indication for osimertinib "
    "in EGFR-mutated NSCLC?"
)
_CLARIFICATION_QUERY = "What does the evidence say about osimertinib resistance?"
_MULTI_TURN_QUERY_1 = (
    "What are the known mechanisms of resistance to osimertinib in EGFR-mutated NSCLC?"
)
_MULTI_TURN_QUERY_2 = (
    "Which first-line clinical trial evidence supports osimertinib in this setting "
    "(FLAURA / NCT02296125)?"
)
_MULTI_TURN_CITATIONS_1 = ["RDOC-PMC6889286"]
_MULTI_TURN_CITATIONS_2 = ["TRIAL-NCT02296125", "LBL-TAGRISSO-OPENFDA"]

_INSUFFICIENT_RAW = [
    "corpus/txt/agent_inputs/quality_issues/empty_upload_stub.txt",
    "corpus/txt/agent_inputs/quality_issues/truncated_protocol_fragment.txt",
]
_MESSY_EXCLUSION_RAW = [
    "corpus/txt/agent_inputs/curation_decisions/CUR-EXCLUDE-PMC4771182.txt",
    "corpus/txt/agent_inputs/curation_decisions/CUR-EXCLUDE-GSE297057.txt",
    "corpus/txt/agent_inputs/curation_decisions/CUR-EXCLUDE-GSE301973.txt",
]

# ING-002 and ING-007 share the same messy upload pool metadata.
_MESSY_UPLOAD = {
    **UI_UPLOAD,
    "request_id": "ING-002-REQ",
    "portal_source_manifest": PORTAL_MANIFEST,
    "uploaded_pmcids": ["PMC6889286", "PMC5447962", "PMC13070087", "PMC13129538", "PMC13143971"],
    "candidate_dataset_ids": [
        "GSE323366", "GSE323365", "GSE272182", "GSE300311", "GSE298111", "GSE297057", "GSE301973",
    ],
}
_MESSY_INGESTION_STAGE = {
    "task": "ingest_and_license_check_uploaded_articles",
    "portal_source_manifest": PORTAL_MANIFEST,
    "uploaded_pmcids": ["PMC6889286", "PMC5447962", "PMC13070087", "PMC13129538", "PMC13143971"],
    "uploaded_exclusion_cards": _MESSY_EXCLUSION_RAW,
    "negative_candidate": {"pmcid": "PMC4771182", "expected": "deny", "decision_entity": "CUR-EXCLUDE-PMC4771182"},
}
_MESSY_LINKING_STAGE = {
    "task": "select_cell_line_or_assay_level_datasets",
    "candidate_dataset_ids": [
        "GSE323366", "GSE323365", "GSE272182", "GSE300311", "GSE298111", "GSE297057", "GSE301973",
    ],
    "negative_candidates": [
        {"dataset_id": "GSE297057", "expected": "exclude", "decision_entity": "CUR-EXCLUDE-GSE297057"},
        {"dataset_id": "GSE301973", "expected": "exclude", "decision_entity": "CUR-EXCLUDE-GSE301973"},
    ],
}

ACCEPTED_ARTICLES = ["RDOC-PMC6889286", "RDOC-PMC5447962", "RDOC-PMC13070087", "RDOC-PMC13129538", "RDOC-PMC13143971"]
ACCEPTED_DATASETS = ["DATASET-GSE323366", "DATASET-GSE323365", "DATASET-GSE272182", "DATASET-GSE300311", "DATASET-GSE298111"]
_ING007_PERSISTED = ACCEPTED_ARTICLES + ACCEPTED_DATASETS
# Entities the metadata & linking agent RAG-retrieves to link the ingested articles against.
# They live in the KB/Vector DB (root catalog), so they are referenced — not staged in input/.
_LINK_TARGETS = ["TRIAL-NCT02296125", "TRIAL-NCT02151981", "REG-NDA208065", "LBL-TAGRISSO-OPENFDA"]
_LINKS = ["LINK-FLAURA-REG-NDA208065", "LINK-REG-NDA208065-LBL-TAGRISSO-OPENFDA"]
_EXTRACTED = ["CMP-CHEMBL3353410", "TGT-CHEMBL203"]


# ============================================================ PHASE 1 — INGESTION SCENARIOS
INGESTION_SCENARIOS: list[dict] = [
    # ------------------------------------------------------------------ ING-001
    {
        "scenario_id": "ING-001", "flow": "ingestion", "phase": 1, "path": "full_approval",
        "title": "Load first-line osimertinib evidence — clean upload, curator approves, persisted",
        "final_outcome": "approved_persisted", "required_human_review": False,
        "trigger": {**UI_UPLOAD, "request_id": "ING-001-REQ",
                    "uploaded_pmcids": ["PMC6889286", "PMC5447962", "PMC13070087", "PMC13129538", "PMC13143971"],
                    "portal_source_manifest": PORTAL_MANIFEST},
        "stages": [
            {
                "stage": "ingestion_translation", "kind": "agent", "agent": "ingestion_translation_agent",
                "agent_input": {"task": "ingest_and_license_check_uploaded_articles",
                                "portal_source_manifest": PORTAL_MANIFEST,
                                "uploaded_pmcids": ["PMC6889286", "PMC5447962", "PMC13070087", "PMC13129538", "PMC13143971"]},
                "input_entities_raw": ACCEPTED_ARTICLES,
                "output_entities": ACCEPTED_ARTICLES,
                "expected_output": {"accepted_article_count": 5, "denied_article_count": 0},
                "decision": "approve", "gate": None,
            },
            {
                "stage": "metadata_linking", "kind": "agent", "agent": "metadata_linking_agent",
                "agent_input": {"task": "extract_entities_and_link_trial_to_regulatory",
                                "retrieved_via_rag": _LINK_TARGETS,
                                "shared_identifiers": ["osimertinib", "Tagrisso", "AZD9291", "NDA208065"]},
                "input_entities": ACCEPTED_ARTICLES,
                "output_entities": _LINKS + _EXTRACTED,
                "expected_output": {"required_links": _LINKS, "extracted_entities": _EXTRACTED},
                "decision": "approve", "gate": None,
            },
            {
                "stage": "curator_approval", "kind": "gate", "actor": CURATOR,
                "gate_record": {"request": "approve_ingested_batch_for_persistence",
                                "reviews_joint_output_of": ["ingestion_translation", "metadata_linking"],
                                "submitted_entities": ACCEPTED_ARTICLES + _LINKS + _EXTRACTED,
                                "reviewer": CURATOR, "decision": "approve"},
                "decision": "approve", "gate": "approved",
            },
            {
                "stage": "persistence", "kind": "sink",
                "persisted": {"target": "cms_knowledge_base", "status": "persisted",
                              "persisted_entities": ACCEPTED_ARTICLES + _LINKS + _EXTRACTED,
                              "persisted_entity_count": len(ACCEPTED_ARTICLES + _LINKS + _EXTRACTED)},
            },
        ],
    },
    # ------------------------------------------------------------------ ING-002
    {
        "scenario_id": "ING-002", "flow": "ingestion", "phase": 1, "path": "guardrail_review",
        "title": "Load with a messy pool — license & PHI guardrails, routed to the curator",
        "final_outcome": "needs_human_review", "required_human_review": True,
        "trigger": {**UI_UPLOAD, "request_id": "ING-002-REQ", "portal_source_manifest": PORTAL_MANIFEST,
                    "uploaded_pmcids": ["PMC6889286", "PMC5447962", "PMC13070087", "PMC13129538", "PMC13143971"],
                    "candidate_dataset_ids": ["GSE323366", "GSE323365", "GSE272182", "GSE300311", "GSE298111", "GSE297057", "GSE301973"]},
        "stages": [
            {
                "stage": "ingestion_translation", "kind": "agent", "agent": "ingestion_translation_agent",
                "agent_input": {"task": "ingest_and_license_check_uploaded_articles",
                                "portal_source_manifest": PORTAL_MANIFEST,
                                "uploaded_pmcids": ["PMC6889286", "PMC5447962", "PMC13070087", "PMC13129538", "PMC13143971"],
                                "uploaded_exclusion_cards": _MESSY_EXCLUSION_RAW,
                                "negative_candidate": {"pmcid": "PMC4771182", "expected": "deny", "decision_entity": "CUR-EXCLUDE-PMC4771182"}},
                "input_entities_raw": ACCEPTED_ARTICLES,
                "input_raw": _MESSY_EXCLUSION_RAW,
                "output_entities": ACCEPTED_ARTICLES + ["CUR-EXCLUDE-PMC4771182"],
                "expected_output": {"accepted_article_count": 5, "denied_article_count": 1, "denied_pmcids": ["PMC4771182"],
                                    "exclusion_card_count": 3},
                "decision": "approve_with_exclusions", "gate": None,
            },
            {
                "stage": "metadata_linking", "kind": "agent", "agent": "metadata_linking_agent",
                "agent_input": {"task": "select_cell_line_or_assay_level_datasets",
                                "candidate_dataset_ids": ["GSE323366", "GSE323365", "GSE272182", "GSE300311", "GSE298111", "GSE297057", "GSE301973"],
                                "negative_candidates": [{"dataset_id": "GSE297057", "expected": "exclude", "decision_entity": "CUR-EXCLUDE-GSE297057"},
                                                        {"dataset_id": "GSE301973", "expected": "exclude", "decision_entity": "CUR-EXCLUDE-GSE301973"}]},
                "input_entities": ACCEPTED_ARTICLES + ["CUR-EXCLUDE-PMC4771182"],
                "output_entities": ACCEPTED_DATASETS + ["CUR-EXCLUDE-GSE297057", "CUR-EXCLUDE-GSE301973"],
                "expected_output": {"accepted_geo_count": 5, "excluded_geo_count": 2},
                "decision": "approve_with_exclusions", "gate": None,
            },
            {
                "stage": "curator_approval", "kind": "gate", "actor": CURATOR,
                "gate_record": {"request": "review_exclusions_before_persistence",
                                "reviews_joint_output_of": ["ingestion_translation", "metadata_linking"],
                                "flagged_exclusions": ["CUR-EXCLUDE-PMC4771182", "CUR-EXCLUDE-GSE297057", "CUR-EXCLUDE-GSE301973"],
                                "reviewer": CURATOR, "decision": "needs_human_review"},
                "decision": "flag_for_human_review", "gate": "denied_pending_human_review",
            },
            {
                "stage": "persistence", "kind": "sink",
                "persisted": {"target": "cms_knowledge_base", "status": "pending_human_review", "persisted_entities": []},
            },
        ],
    },
    # ------------------------------------------------------------------ ING-003
    {
        "scenario_id": "ING-003", "flow": "ingestion", "phase": 1, "path": "synthetic_provenance",
        "title": "Load synthetic ELN/LIMS records — curator approves with required labeling, persisted",
        "final_outcome": "approved_with_required_labeling", "required_human_review": False,
        "trigger": {**UI_UPLOAD, "request_id": "ING-003-REQ", "uploaded_files": SYNTHETIC_RAW,
                    "note": "synthetic ELN/LIMS derived from public GEO structure — no patient data"},
        "stages": [
            {
                "stage": "ingestion_translation", "kind": "agent", "agent": "ingestion_translation_agent",
                "agent_input": {"task": "ingest_synthetic_eln_lims", "uploaded_files": SYNTHETIC_RAW},
                "input_raw": SYNTHETIC_RAW,
                "output_entities": ["SYN-LIMS-001", "SYN-LIMS-010"],
                "expected_output": {"synthetic_record_count": 2, "provenance": "synthetic_from_public_structure"},
                "decision": "approve", "gate": None,
            },
            {
                "stage": "metadata_linking", "kind": "agent", "agent": "metadata_linking_agent",
                "agent_input": {"task": "associate_synthetic_samples_to_geo_series",
                                "samples": ["SYN-LIMS-001", "SYN-LIMS-010"], "retrieved_via_rag": ["DATASET-GSE323366"]},
                "input_entities": ["SYN-LIMS-001", "SYN-LIMS-010"],
                "output_entities": ["DATASET-GSE323366"],
                "expected_output": {"associations": [{"sample": "SYN-LIMS-001", "geo_series": "GSE323366"},
                                                     {"sample": "SYN-LIMS-010", "geo_series": "GSE323366"}]},
                "decision": "approve", "gate": None,
            },
            {
                "stage": "curator_approval", "kind": "gate", "actor": CURATOR,
                "gate_record": {"request": "approve_synthetic_records_with_labeling",
                                "reviews_joint_output_of": ["ingestion_translation", "metadata_linking"],
                                "submitted_entities": ["SYN-LIMS-001", "SYN-LIMS-010", "DATASET-GSE323366"],
                                "required_label": "synthetic_from_public_structure",
                                "reviewer": CURATOR, "decision": "approve_with_required_labeling"},
                "decision": "approve_with_required_labeling", "gate": "approved_with_labeling",
            },
            {
                "stage": "persistence", "kind": "sink",
                "persisted": {"target": "cms_knowledge_base", "status": "persisted_with_required_labeling",
                              "persisted_entities": ["SYN-LIMS-001", "SYN-LIMS-010", "DATASET-GSE323366"],
                              "required_label": "synthetic_from_public_structure"},
            },
        ],
    },
    # ------------------------------------------------------------------ ING-004
    {
        "scenario_id": "ING-004", "flow": "ingestion", "phase": 1, "path": "sensitive_blocked",
        "title": "Load a patient-derived candidate — blocked by the curator, never persisted",
        "final_outcome": "denied_not_persisted", "required_human_review": True,
        "trigger": {**UI_UPLOAD, "request_id": "ING-004-REQ", "candidate_dataset_ids": ["GSE301973"],
                    "note": "GSE301973 references before/after treatment specimens (patient-derived)"},
        "stages": [
            {
                "stage": "ingestion_translation", "kind": "agent", "agent": "ingestion_translation_agent",
                "agent_input": {"task": "ingest_candidate_dataset_metadata_only",
                                "candidate_dataset_ids": ["GSE301973"]},
                "input_raw": ["corpus/txt/agent_inputs/curation_decisions/CUR-EXCLUDE-GSE301973.txt"],
                "output_entities": [],
                "expected_output": {"normalized_count": 0, "flagged_candidate": "GSE301973",
                                    "note": "metadata only — deferred to the knowledge curator"},
                "decision": "defer_to_human_approval", "gate": None,
            },
            {
                "stage": "metadata_linking", "kind": "agent", "agent": "metadata_linking_agent",
                "agent_input": {"task": "link_admitted_entities", "admitted_entities": []},
                "input_entities": [],
                "output_entities": [],
                "expected_output": {"link_count": 0, "reason": "candidate_not_admitted"},
                "decision": "no_action", "gate": None,
            },
            {
                "stage": "curator_approval", "kind": "gate", "actor": CURATOR,
                "gate_record": {"request": "approve_candidate_for_persistence", "candidate": "GSE301973",
                                "reviews_joint_output_of": ["ingestion_translation", "metadata_linking"],
                                "reviewer": CURATOR, "decision": "deny", "reason": "patient_derived_specimens"},
                "decision": "deny", "gate": "denied",
            },
            {
                "stage": "persistence", "kind": "sink",
                "persisted": {"target": "cms_knowledge_base", "status": "denied_not_persisted", "persisted_entities": []},
            },
        ],
    },
    # ------------------------------------------------------------------ ING-005
    {
        "scenario_id": "ING-005", "flow": "ingestion", "phase": 1, "path": "insufficient_data",
        "title": "Load an empty and truncated batch — insufficient data, nothing persisted",
        "final_outcome": "insufficient_data_not_persisted", "required_human_review": False,
        "trigger": {**UI_UPLOAD, "request_id": "ING-005-REQ",
                    "uploaded_files": _INSUFFICIENT_RAW,
                    "note": "batch contains an empty file and a truncated protocol fragment"},
        "stages": [
            {
                "stage": "ingestion_translation", "kind": "agent", "agent": "ingestion_translation_agent",
                "agent_input": {"task": "ingest_uploaded_batch", "uploaded_files": _INSUFFICIENT_RAW},
                "input_raw": _INSUFFICIENT_RAW,
                "output_entities": [],
                "expected_output": {"processed_item_count": 2, "extractable_content_count": 0,
                                    "quality_issues": ["empty_file", "truncated_content"]},
                "decision": "insufficient_data", "gate": None,
            },
            {
                "stage": "metadata_linking", "kind": "agent", "agent": "metadata_linking_agent",
                "agent_input": {"task": "link_admitted_entities", "admitted_entities": []},
                "input_entities": [],
                "output_entities": [],
                "expected_output": {"link_count": 0, "reason": "no_extractable_content"},
                "decision": "insufficient_data", "gate": None,
            },
            {
                "stage": "curator_approval", "kind": "gate", "actor": CURATOR,
                "gate_record": {"request": "review_insufficient_batch",
                                "reviews_joint_output_of": ["ingestion_translation", "metadata_linking"],
                                "reviewer": CURATOR, "decision": "deny", "reason": "insufficient_extractable_content"},
                "decision": "deny", "gate": "denied",
            },
            {
                "stage": "persistence", "kind": "sink",
                "persisted": {"target": "cms_knowledge_base", "status": "denied_not_persisted", "persisted_entities": []},
            },
        ],
    },
    # ------------------------------------------------------------------ ING-007
    {
        "scenario_id": "ING-007", "flow": "ingestion", "phase": 1, "path": "approve_after_review",
        "title": "Messy pool reviewed by curator — exclusions accepted, admitted content persisted",
        "final_outcome": "approved_with_exclusions_persisted", "required_human_review": True,
        "trigger": {**_MESSY_UPLOAD, "request_id": "ING-007-REQ"},
        "stages": [
            {
                "stage": "ingestion_translation", "kind": "agent", "agent": "ingestion_translation_agent",
                "agent_input": _MESSY_INGESTION_STAGE,
                "input_entities_raw": ACCEPTED_ARTICLES,
                "input_raw": _MESSY_EXCLUSION_RAW,
                "output_entities": ACCEPTED_ARTICLES + ["CUR-EXCLUDE-PMC4771182"],
                "expected_output": {"accepted_article_count": 5, "denied_article_count": 1, "denied_pmcids": ["PMC4771182"],
                                    "exclusion_card_count": 3},
                "decision": "approve_with_exclusions", "gate": None,
            },
            {
                "stage": "metadata_linking", "kind": "agent", "agent": "metadata_linking_agent",
                "agent_input": _MESSY_LINKING_STAGE,
                "input_entities": ACCEPTED_ARTICLES + ["CUR-EXCLUDE-PMC4771182"],
                "output_entities": ACCEPTED_DATASETS + ["CUR-EXCLUDE-GSE297057", "CUR-EXCLUDE-GSE301973"],
                "expected_output": {"accepted_geo_count": 5, "excluded_geo_count": 2},
                "decision": "approve_with_exclusions", "gate": None,
            },
            {
                "stage": "curator_approval", "kind": "gate", "actor": CURATOR,
                "gate_record": {"request": "approve_admitted_content_after_exclusion_review",
                                "reviews_joint_output_of": ["ingestion_translation", "metadata_linking"],
                                "flagged_exclusions": ["CUR-EXCLUDE-PMC4771182", "CUR-EXCLUDE-GSE297057", "CUR-EXCLUDE-GSE301973"],
                                "submitted_entities": _ING007_PERSISTED,
                                "reviewer": CURATOR, "decision": "approve_with_exclusions"},
                "decision": "approve_with_exclusions", "gate": "approved",
            },
            {
                "stage": "persistence", "kind": "sink",
                "persisted": {"target": "cms_knowledge_base", "status": "persisted_with_exclusions",
                              "persisted_entities": _ING007_PERSISTED,
                              "persisted_entity_count": len(_ING007_PERSISTED),
                              "excluded_entities": ["CUR-EXCLUDE-PMC4771182", "CUR-EXCLUDE-GSE297057", "CUR-EXCLUDE-GSE301973"]},
            },
        ],
    },
]


# ============================================================ PHASE 2 — SEARCH SCENARIOS
SEARCH_SCENARIOS: list[dict] = [
    # ------------------------------------------------------------------ QRY-001
    {
        "scenario_id": "QRY-001", "flow": "search", "phase": 2, "path": "no_data", "kb_state": "empty",
        "title": "Query an empty knowledge base — no grounded answer (demo step 1)",
        "final_outcome": "no_grounded_answer", "required_human_review": False,
        "trigger": {**UI_QUERY, "request_id": "QRY-001-REQ", "query": _GROUNDED_QUERY, "kb_state": "empty"},
        "stages": [
            {
                "stage": "search_chat", "kind": "agent", "agent": "search_chat_agent",
                "agent_input": {"task": "answer_grounded_query", "query": _GROUNDED_QUERY, "retrieval_scope_entities": []},
                "prompt": _GROUNDED_QUERY,
                "input_entities": [],
                "output_entities": [],
                "expected_output": {"decision": "no_grounded_answer", "reason": "knowledge_base_empty", "citation_count": 0},
                "decision": "no_grounded_answer", "gate": None,
            },
            {
                "stage": "curation_compliance", "kind": "agent", "agent": "curation_compliance_agent",
                "agent_input": {"task": "review_answer_before_return", "draft_decision": "no_grounded_answer"},
                "input_entities": [],
                "output_entities": [],
                "expected_output": {"decision": "confirm_no_data_response", "sensitive_content_found": False, "required_human_review": False},
                "decision": "confirm_no_data_response", "gate": None,
            },
            {
                "stage": "compliance_approval", "kind": "gate", "actor": COMPLIANCE_OWNER,
                "gate_record": {"request": "approve_response_before_return",
                                "reviews_joint_output_of": ["search_chat", "curation_compliance"],
                                "reviewer": COMPLIANCE_OWNER, "decision": "approve"},
                "decision": "approve", "gate": "approved",
            },
            {
                "stage": "response", "kind": "output",
                "response": {"returned": "No grounded information is available yet — ingest knowledge first.",
                             "citations": [], "raw_source_trace": False},
            },
        ],
    },
    # ------------------------------------------------------------------ QRY-002
    {
        "scenario_id": "QRY-002", "flow": "search", "phase": 2, "path": "grounded", "kb_state": "populated",
        "title": "Query a populated knowledge base — grounded answer with citations (demo step 3)",
        "final_outcome": "answer_with_citations", "required_human_review": False,
        "trigger": {**UI_QUERY, "request_id": "QRY-002-REQ", "query": _GROUNDED_QUERY, "kb_state": "populated",
                    "depends_on_ingestion": "ING-001 (entities must already be persisted)"},
        "stages": [
            {
                "stage": "search_chat", "kind": "agent", "agent": "search_chat_agent",
                "agent_input": {"task": "answer_grounded_query", "query": _GROUNDED_QUERY,
                                "retrieval_scope_entities": _RETRIEVAL_SCOPE},
                "prompt": _GROUNDED_QUERY,
                "input_entities": _RETRIEVAL_SCOPE,
                "output_entities": [],
                "expected_output": {"decision": "answer_with_citations", "minimum_citation_count": 2,
                                    "must_include_raw_source_trace": True,
                                    "expected_citations": _GROUNDED_CITATIONS, "expected_answer_points": _ANSWER_POINTS},
                "decision": "answer_with_citations", "gate": None,
            },
            {
                "stage": "curation_compliance", "kind": "agent", "agent": "curation_compliance_agent",
                "agent_input": {"task": "review_answer_before_return", "draft_answer_citations": _GROUNDED_CITATIONS,
                                "enforced_policy_refs": ["HLS-TRIAL-300", "HLS-LIC-200"]},
                "input_entities": _GROUNDED_CITATIONS,
                "output_entities": [],
                "expected_output": {"decision": "approve_response", "sensitive_content_found": False, "required_human_review": False},
                "decision": "approve_response", "gate": None,
            },
            {
                "stage": "compliance_approval", "kind": "gate", "actor": COMPLIANCE_OWNER,
                "gate_record": {"request": "approve_response_before_return",
                                "reviews_joint_output_of": ["search_chat", "curation_compliance"],
                                "approved_citations": _GROUNDED_CITATIONS,
                                "reviewer": COMPLIANCE_OWNER, "decision": "approve"},
                "decision": "approve", "gate": "approved",
            },
            {
                "stage": "response", "kind": "output",
                "response": {"returned": "Grounded answer on osimertinib resistance mechanisms and first-line evidence.",
                             "citations": _GROUNDED_CITATIONS, "raw_source_trace": True},
            },
        ],
    },
    # ------------------------------------------------------------------ QRY-003
    {
        "scenario_id": "QRY-003", "flow": "search", "phase": 2, "path": "eu_policy_gap", "kb_state": "populated",
        "title": "EU-scoped query — grounded answer missing regional policy context",
        "final_outcome": "flagged_for_compliance_review", "required_human_review": True,
        "trigger": {**UI_QUERY, "request_id": "QRY-003-REQ", "query": _EU_QUERY, "kb_state": "populated",
                    "depends_on_ingestion": "ING-001 (entities must already be persisted)"},
        "stages": [
            {
                "stage": "search_chat", "kind": "agent", "agent": "search_chat_agent",
                "agent_input": {"task": "answer_grounded_query", "query": _EU_QUERY,
                                "retrieval_scope_entities": _RETRIEVAL_SCOPE},
                "prompt": _EU_QUERY,
                "input_entities": _RETRIEVAL_SCOPE,
                "output_entities": [],
                "expected_output": {"decision": "answer_with_citations", "minimum_citation_count": 2,
                                    "must_include_raw_source_trace": True,
                                    "expected_citations": ["TRIAL-NCT02296125", "LBL-TAGRISSO-OPENFDA"],
                                    "missing_regional_context": "EU/EMA EPAR not cited"},
                "decision": "answer_with_citations", "gate": None,
            },
            {
                "stage": "curation_compliance", "kind": "agent", "agent": "curation_compliance_agent",
                "agent_input": {"task": "review_answer_before_return",
                                "draft_answer_citations": ["TRIAL-NCT02296125", "LBL-TAGRISSO-OPENFDA"],
                                "enforced_policy_refs": ["HLS-REGION-EU-400", "HLS-LIC-200", "HLS-TRIAL-300"]},
                "input_entities": ["TRIAL-NCT02296125", "LBL-TAGRISSO-OPENFDA"],
                "output_entities": [],
                "expected_output": {"decision": "flag_for_review",
                                    "flags": ["missing_eu_regional_policy_reference"],
                                    "policy_refs": ["HLS-REGION-EU-400"],
                                    "sensitive_content_found": False, "required_human_review": True},
                "decision": "flag_for_review", "gate": None,
            },
            {
                "stage": "compliance_approval", "kind": "gate", "actor": COMPLIANCE_OWNER,
                "gate_record": {"request": "review_flagged_response_before_return",
                                "reviews_joint_output_of": ["search_chat", "curation_compliance"],
                                "flags": ["missing_eu_regional_policy_reference"],
                                "reviewer": COMPLIANCE_OWNER, "decision": "approve_with_flags"},
                "decision": "approve_with_flags", "gate": "approved_with_flags",
            },
            {
                "stage": "response", "kind": "output",
                "response": {"returned": "Grounded answer returned with compliance flag for missing EU regional policy context.",
                             "citations": ["TRIAL-NCT02296125", "LBL-TAGRISSO-OPENFDA"],
                             "raw_source_trace": True, "compliance_flags": ["missing_eu_regional_policy_reference"]},
            },
        ],
    },
    # ------------------------------------------------------------------ QRY-004
    {
        "scenario_id": "QRY-004", "flow": "search", "phase": 2, "path": "clarification_needed", "kb_state": "populated",
        "title": "Ambiguous resistance query — clarification needed before retrieval",
        "final_outcome": "clarification_needed", "required_human_review": False,
        "trigger": {**UI_QUERY, "request_id": "QRY-004-REQ", "query": _CLARIFICATION_QUERY, "kb_state": "populated"},
        "stages": [
            {
                "stage": "search_chat", "kind": "agent", "agent": "search_chat_agent",
                "agent_input": {"task": "answer_grounded_query", "query": _CLARIFICATION_QUERY,
                                "retrieval_scope_entities": []},
                "prompt": _CLARIFICATION_QUERY,
                "input_entities": [],
                "output_entities": [],
                "expected_output": {"decision": "clarification_needed",
                                    "reason": "missing_scope_study_region_or_time_window",
                                    "citation_count": 0},
                "decision": "clarification_needed", "gate": None,
            },
            {
                "stage": "response", "kind": "output",
                "response": {"returned": "Clarification needed: specify study, region, or time window for osimertinib resistance evidence.",
                             "citations": [], "raw_source_trace": False},
            },
        ],
    },
    # ------------------------------------------------------------------ QRY-005
    {
        "scenario_id": "QRY-005", "flow": "search", "phase": 2, "path": "multi_turn_curate", "kb_state": "populated",
        "title": "Multi-turn grounded chat — Curate reviews accumulated responses",
        "final_outcome": "answer_with_citations", "required_human_review": False,
        "trigger": {**UI_QUERY, "request_id": "QRY-005-REQ", "kb_state": "populated",
                    "depends_on_ingestion": "ING-001 (entities must already be persisted)",
                    "note": "run turn 1 then turn 2 in the same session before Curate"},
        "stages": [
            {
                "stage": "search_chat_turn_1", "kind": "agent", "agent": "search_chat_agent",
                "agent_input": {"task": "answer_grounded_query", "query": _MULTI_TURN_QUERY_1,
                                "retrieval_scope_entities": ["RDOC-PMC6889286"]},
                "prompt": _MULTI_TURN_QUERY_1,
                "input_entities": ["RDOC-PMC6889286"],
                "output_entities": [],
                "expected_output": {"decision": "answer_with_citations", "turn": 1,
                                    "expected_citations": _MULTI_TURN_CITATIONS_1,
                                    "expected_answer_points": [_ANSWER_POINTS[0], _ANSWER_POINTS[1]]},
                "decision": "answer_with_citations", "gate": None,
            },
            {
                "stage": "search_chat_turn_2", "kind": "agent", "agent": "search_chat_agent",
                "agent_input": {"task": "answer_grounded_query", "query": _MULTI_TURN_QUERY_2,
                                "retrieval_scope_entities": ["TRIAL-NCT02296125", "LBL-TAGRISSO-OPENFDA"]},
                "prompt": _MULTI_TURN_QUERY_2,
                "input_entities": ["TRIAL-NCT02296125", "LBL-TAGRISSO-OPENFDA"],
                "output_entities": [],
                "expected_output": {"decision": "answer_with_citations", "turn": 2,
                                    "expected_citations": _MULTI_TURN_CITATIONS_2,
                                    "expected_answer_points": [_ANSWER_POINTS[2], _ANSWER_POINTS[3]]},
                "decision": "answer_with_citations", "gate": None,
            },
            {
                "stage": "curation_compliance", "kind": "agent", "agent": "curation_compliance_agent",
                "agent_input": {"task": "review_session_before_return",
                                "chat_responses_reviewed": 2,
                                "draft_answer_citations": _MULTI_TURN_CITATIONS_1 + _MULTI_TURN_CITATIONS_2,
                                "enforced_policy_refs": ["HLS-TRIAL-300", "HLS-LIC-200"]},
                "input_entities": _MULTI_TURN_CITATIONS_1 + _MULTI_TURN_CITATIONS_2,
                "output_entities": [],
                "expected_output": {"decision": "approve_response", "chat_responses_reviewed": 2,
                                    "sensitive_content_found": False, "required_human_review": False},
                "decision": "approve_response", "gate": None,
            },
            {
                "stage": "compliance_approval", "kind": "gate", "actor": COMPLIANCE_OWNER,
                "gate_record": {"request": "approve_multi_turn_response_before_return",
                                "reviews_joint_output_of": ["search_chat_turn_1", "search_chat_turn_2", "curation_compliance"],
                                "approved_citations": _MULTI_TURN_CITATIONS_1 + _MULTI_TURN_CITATIONS_2,
                                "reviewer": COMPLIANCE_OWNER, "decision": "approve"},
                "decision": "approve", "gate": "approved",
            },
            {
                "stage": "response", "kind": "output",
                "response": {"returned": "Multi-turn grounded session on resistance mechanisms and FLAURA first-line evidence.",
                             "citations": _MULTI_TURN_CITATIONS_1 + _MULTI_TURN_CITATIONS_2,
                             "raw_source_trace": True, "turn_count": 2},
            },
        ],
    },
]

SCENARIOS: list[dict] = INGESTION_SCENARIOS + SEARCH_SCENARIOS
SCENARIOS_BY_ID: dict[str, dict] = {s["scenario_id"]: s for s in SCENARIOS}


def scenario_folder(scenario: dict) -> str:
    """e.g. 'ING-001_full_approval' / 'QRY-001_no_data' — mirrors loan's APP-XXX_<reason>."""
    return f"{scenario['scenario_id']}_{scenario['path']}"


def materialized_stages(scenario: dict) -> list[dict]:
    """The stages that get a raw-layer folder (data-consuming agents + the response output)."""
    return [s for s in scenario["stages"] if s["kind"] in MATERIALIZED_KINDS]


def stage_folder(scenario: dict, stage: dict) -> str:
    """'NN_<stage>' — NN is the 1-based position among the scenario's materialized stages."""
    idx = materialized_stages(scenario).index(stage) + 1
    return f"{idx:02d}_{stage['stage']}"


def stage_primary(stage: dict) -> tuple[str | None, str, object]:
    """Return (filename, key, content) for a stage's primary payload, keyed by its kind."""
    filename, key = STAGE_PRIMARY[stage["kind"]]
    return filename, key, stage[key]


def demo_prefix(scenario_id: str) -> int:
    """1-based position of a scenario in the headline DEMO_SCENARIO sequence."""
    return DEMO_SEQUENCE.index(scenario_id) + 1


def case_folder(scenario: dict) -> str:
    """Demo folder name under dataset-seed/cases/."""
    return CASE_FOLDERS[scenario["scenario_id"]]
