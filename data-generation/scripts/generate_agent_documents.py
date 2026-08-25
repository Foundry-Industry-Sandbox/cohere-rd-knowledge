#!/usr/bin/env python3
"""
Generate multi-format agent input replicas from normalized HLS dataset entities.

Unlike the FSI and retail scenarios, the HLS Raw Layer already contains mixed
public source formats: XML, JSON, PDF, HTML, TXT, and CSV. Replicating every raw
file into every format would add noise. This script instead creates compact
evidence cards for representative source-level entities, preserving the same
canonical facts across TXT, Markdown, HTML, and PDF.

Output:
  data-generation/corpus/{txt,md,html,pdf}/agent_inputs/...

These replicas are for extraction-consistency tests. They are not new source
truth; every card includes raw source traces back to corpus/. They live in
the canonical corpus (cross-cutting consistency assets), not per-scenario demo folders.
"""

from __future__ import annotations

import argparse
import html
import json
import shutil
import textwrap
from dataclasses import dataclass
from hashlib import sha256
from pathlib import Path
from typing import Any


from generate_normalized_layers import CATALOG_PATH, build_entity_index, load_json

SCRIPTS = Path(__file__).resolve().parent
DATA_GEN = SCRIPTS.parent
BASE = DATA_GEN
RAW = DATA_GEN / "corpus"
LEGACY_OUT = RAW / "format_replicas"
MANIFEST = RAW / "agent_document_manifest.json"
FORMAT_ORDER = ["txt", "md", "html", "pdf"]
AGENT_INPUT_ROOT = "agent_inputs"


@dataclass
class AgentDoc:
    document_id: str
    category: str
    title: str
    subtitle: str
    sections: list[tuple[str, list[tuple[str, str]]]]
    source_entity_path: str


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def write_text(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content.rstrip() + "\n", encoding="utf-8")
    print(f"  {path.relative_to(BASE)}")


def write_bytes(path: Path, content: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(content)
    print(f"  {path.relative_to(BASE)}")


def rel(path: Path) -> str:
    return str(path.relative_to(BASE))


def clean_value(value: Any) -> str:
    if value is None:
        return ""
    if isinstance(value, (list, tuple)):
        return "; ".join(clean_value(v) for v in value if clean_value(v))
    if isinstance(value, dict):
        return json.dumps(value, sort_keys=True, ensure_ascii=False)
    return " ".join(str(value).split())


def truncate(value: Any, limit: int = 900) -> str:
    text = clean_value(value)
    if len(text) <= limit:
        return text
    return text[: limit - 3].rstrip() + "..."


def slug(value: str) -> str:
    keep = []
    for ch in value:
        if ch.isalnum() or ch in {"-", "_", "."}:
            keep.append(ch)
        else:
            keep.append("_")
    return "".join(keep).strip("_")


def add_common_sections(entity: dict[str, Any], sections: list[tuple[str, list[tuple[str, str]]]]) -> list[tuple[str, list[tuple[str, str]]]]:
    raw_sources = entity.get("raw_sources", [])
    sections.append(
        (
            "Provenance",
            [
                ("Provenance", clean_value(entity.get("provenance"))),
                ("Privacy posture", clean_value(entity.get("privacy_posture"))),
                ("Raw source trace", clean_value(raw_sources[:6])),
            ],
        )
    )
    return sections


def doc_from_research(e: dict[str, Any]) -> AgentDoc:
    sections = [
        (
            "Article Identity",
            [
                ("Document ID", e["document_id"]),
                ("PMCID", e["source_identifiers"].get("pmcid")),
                ("PMID", e["source_identifiers"].get("pmid")),
                ("Title", e.get("title")),
                ("Journal", e.get("journal")),
                ("Publication date", e.get("publication_date")),
                ("License", e.get("license")),
            ],
        ),
        (
            "Extractable Content",
            [
                ("Primary entities", e.get("primary_entities", [])),
                ("Keywords", e.get("keywords", [])[:12]),
                ("Abstract", truncate(e.get("abstract"), 1400)),
            ],
        ),
    ]
    return AgentDoc(e["document_id"], "research_documents", e["title"], "Research article evidence card", add_common_sections(e, sections), f"entity/{e['document_id']}.json")


def doc_from_trial(e: dict[str, Any]) -> AgentDoc:
    sections = [
        (
            "Trial Identity",
            [
                ("Trial ID", e["trial_id"]),
                ("Acronym", e.get("acronym")),
                ("Brief title", e.get("brief_title")),
                ("Overall status", e.get("overall_status")),
                ("Lead sponsor", e.get("lead_sponsor")),
                ("Collaborators", e.get("collaborators", [])),
            ],
        ),
        (
            "Design And Outcomes",
            [
                ("Phases", e.get("phases", [])),
                ("Enrollment", e.get("enrollment", {})),
                ("Conditions", e.get("conditions", [])),
                ("Interventions", [i.get("name") for i in e.get("interventions", [])[:8]]),
                ("Primary outcomes", [o.get("measure") for o in e.get("primary_outcomes", [])]),
                ("Has results", e.get("has_results")),
            ],
        ),
        (
            "Public Documents",
            [
                ("Documents", [d.get("filename") for d in e.get("public_documents", [])]),
            ],
        ),
    ]
    return AgentDoc(f"TRIAL-{e['trial_id']}", "clinical_trials", e.get("brief_title", e["trial_id"]), "Clinical trial evidence card", add_common_sections(e, sections), f"entity/TRIAL-{e['trial_id']}.json")


def doc_from_dataset(e: dict[str, Any]) -> AgentDoc:
    sections = [
        (
            "Dataset Identity",
            [
                ("Dataset ID", e["dataset_id"]),
                ("Title", e.get("title")),
                ("Taxon", e.get("taxon")),
                ("Sample count", e.get("sample_count")),
                ("Selected reason", e.get("selected_reason")),
            ],
        ),
        (
            "Experimental Context",
            [
                ("Assay context", e.get("assay_context", [])),
                ("Sample accessions", e.get("sample_accessions", [])[:20]),
                ("Summary", truncate(e.get("summary"), 1400)),
            ],
        ),
    ]
    return AgentDoc(f"DATASET-{e['dataset_id']}", "experimental_datasets", e.get("title", e["dataset_id"]), "Experimental dataset evidence card", add_common_sections(e, sections), f"entity/DATASET-{e['dataset_id']}.json")


def doc_from_regulatory(e: dict[str, Any]) -> AgentDoc:
    document_id = e["document_id"]
    sections = [
        (
            "Regulatory Identity",
            [
                ("Document ID", document_id),
                ("Document type", e.get("document_type")),
                ("Source system", e.get("source_system")),
                ("Application number", e.get("application_number")),
                ("Brand name", e.get("brand_name")),
                ("Generic name", e.get("generic_name")),
                ("Sponsor/manufacturer", e.get("sponsor_name") or e.get("manufacturer")),
            ],
        )
    ]
    if e.get("document_type") == "product_label":
        sections.append(
            (
                "Label Extract",
                [
                    ("Effective time", e.get("effective_time")),
                    ("Route", e.get("route", [])),
                    ("Indications", truncate(e.get("indications_and_usage"), 1200)),
                    ("Warnings", truncate(e.get("warnings_and_precautions"), 900)),
                ],
            )
        )
    else:
        sections.append(
            (
                "Regulatory Extract",
                [
                    ("Products", e.get("products", [])[:4]),
                    ("Submission count", len(e.get("submissions", []))),
                    ("Priority submissions", e.get("priority_submission_count")),
                    ("URL", e.get("url")),
                ],
            )
        )
    return AgentDoc(document_id, "regulatory_submissions", document_id, "Regulatory evidence card", add_common_sections(e, sections), f"entity/{document_id}.json")


def doc_from_policy(e: dict[str, Any]) -> AgentDoc:
    sections = [
        (
            "Policy Rule",
            [
                ("Policy ID", e["policy_id"]),
                ("Policy ref", e["policy_ref"]),
                ("Title", e.get("title")),
                ("Rule", e.get("rule")),
                ("Threshold", e.get("threshold")),
                ("Action", e.get("action")),
                ("Source refs", e.get("source_refs", [])),
            ],
        )
    ]
    return AgentDoc(e["policy_id"], "policy_rag", e.get("title", e["policy_id"]), "Policy RAG evidence card", add_common_sections(e, sections), f"entity/{e['policy_id']}.json")


def doc_from_curation(e: dict[str, Any]) -> AgentDoc:
    sections = [
        (
            "Curation Decision",
            [
                ("Decision ID", e["decision_id"]),
                ("Source ID", e.get("source_id")),
                ("Source type", e.get("source_type")),
                ("Decision", e.get("decision")),
                ("Reason", e.get("reason")),
                ("Required human review", e.get("required_human_review")),
                ("Policy refs", e.get("policy_refs", [])),
                ("Curator", e.get("curator")),
                ("Decision date", e.get("decision_date")),
            ],
        )
    ]
    return AgentDoc(e["decision_id"], "curation_decisions", e["decision_id"], "Curation and compliance decision card", add_common_sections(e, sections), f"entity/{e['decision_id']}.json")


def doc_from_eln_lims_digest(samples: list[dict[str, Any]]) -> AgentDoc:
    rows = []
    for sample in samples:
        rows.append(
            (
                sample["sample_id"],
                f"{sample.get('model')} | {sample.get('condition')} | {sample.get('assay')} | {sample.get('source_geo_series')}",
            )
        )
    entity = {
        "provenance": "synthetic_from_public_structure",
        "privacy_posture": "fictional/synthetic operational record; no patient data",
        "raw_sources": [
            "corpus/csv/synthetic_eln_lims/lims_sample_manifest.csv",
            "corpus/txt/synthetic_eln_lims/eln_experiment_notebook.txt",
        ],
    }
    sections = [
        (
            "Synthetic ELN/LIMS Digest",
            [
                ("Document ID", "SYNTHETIC-ELN-LIMS-DIGEST"),
                ("Source system", "synthetic_eln_lims"),
                ("Sample count", len(samples)),
                ("Purpose", "Cross-format validation of lab-style operational records derived from public source structure."),
            ],
        ),
        ("Sample Rows", rows),
    ]
    return AgentDoc(
        "SYNTHETIC-ELN-LIMS-DIGEST",
        "synthetic_eln_lims",
        "Synthetic ELN/LIMS Digest",
        "Synthetic operational evidence card",
        add_common_sections(entity, sections),
        "entity/SYN-LIMS-*.json",
    )


def collect_docs(categories: set[str]) -> list[AgentDoc]:
    catalog = load_json(CATALOG_PATH)
    index = build_entity_index(catalog)
    docs: list[AgentDoc] = []

    def include(name: str) -> bool:
        return "all" in categories or name in categories

    if include("research_documents"):
        docs.extend(doc_from_research(e) for e in sorted(index.values(), key=lambda x: x.get("document_id", "")) if e.get("document_type") == "research_document")
    if include("clinical_trials"):
        docs.extend(doc_from_trial(e) for e in index.values() if e.get("document_type") == "clinical_trial")
    if include("experimental_datasets"):
        docs.extend(doc_from_dataset(e) for e in index.values() if e.get("document_type") == "experimental_dataset")
    if include("regulatory_submissions"):
        docs.extend(doc_from_regulatory(e) for e in index.values() if e.get("document_type") in {"regulatory_application", "product_label"})
    if include("policy_rag"):
        docs.extend(doc_from_policy(e) for e in index.values() if e.get("document_type") == "policy_rag_rule")
    if include("curation_decisions"):
        docs.extend(doc_from_curation(e) for e in index.values() if e.get("document_type") == "curation_decision")
    if include("synthetic_eln_lims"):
        lims = [e for e in index.values() if e.get("document_type") == "synthetic_lims_sample"]
        if lims:
            docs.append(doc_from_eln_lims_digest(sorted(lims, key=lambda x: x["sample_id"])))

    return docs


def render_plain(doc: AgentDoc) -> str:
    lines = [
        doc.title,
        "=" * min(len(doc.title), 80),
        doc.subtitle,
        "",
        f"Document ID: {doc.document_id}",
        f"Category: {doc.category}",
        f"Source entity: {doc.source_entity_path}",
        "",
    ]
    for heading, rows in doc.sections:
        lines += [heading, "-" * len(heading)]
        for key, value in rows:
            lines.append(f"{key}: {truncate(value, 1200)}")
        lines.append("")
    return "\n".join(lines)


def render_markdown(doc: AgentDoc) -> str:
    lines = [
        f"# {doc.title}",
        "",
        doc.subtitle,
        "",
        f"- Document ID: `{doc.document_id}`",
        f"- Category: `{doc.category}`",
        f"- Source entity: `{doc.source_entity_path}`",
        "",
    ]
    for heading, rows in doc.sections:
        lines += [f"## {heading}", "", "| Field | Value |", "| --- | --- |"]
        for key, value in rows:
            cell = truncate(value, 1200).replace("|", "\\|")
            lines.append(f"| {key} | {cell} |")
        lines.append("")
    return "\n".join(lines)


def render_html(doc: AgentDoc) -> str:
    parts = [
        "<!doctype html>",
        "<html lang=\"en\">",
        "<head>",
        "  <meta charset=\"utf-8\">",
        f"  <title>{html.escape(doc.title)}</title>",
        "  <style>body{font-family:Arial,sans-serif;max-width:920px;margin:36px auto;color:#1f2937}"
        "table{border-collapse:collapse;width:100%;margin-bottom:24px}th,td{border:1px solid #d1d5db;padding:8px;vertical-align:top}"
        "th{background:#e5edf7;text-align:left}.meta{color:#64748b}</style>",
        "</head>",
        "<body>",
        f"<h1>{html.escape(doc.title)}</h1>",
        f"<p class=\"meta\">{html.escape(doc.subtitle)}</p>",
        "<ul>",
        f"<li><strong>Document ID:</strong> {html.escape(doc.document_id)}</li>",
        f"<li><strong>Category:</strong> {html.escape(doc.category)}</li>",
        f"<li><strong>Source entity:</strong> {html.escape(doc.source_entity_path)}</li>",
        "</ul>",
    ]
    for heading, rows in doc.sections:
        parts.append(f"<h2>{html.escape(heading)}</h2>")
        parts.append("<table><thead><tr><th>Field</th><th>Value</th></tr></thead><tbody>")
        for key, value in rows:
            parts.append(f"<tr><td>{html.escape(key)}</td><td>{html.escape(truncate(value, 1400))}</td></tr>")
        parts.append("</tbody></table>")
    parts += ["</body>", "</html>"]
    return "\n".join(parts)


def pdf_escape(value: str) -> str:
    return value.replace("\\", "\\\\").replace("(", "\\(").replace(")", "\\)")


def wrap_pdf_lines(text: str) -> list[str]:
    lines = []
    for raw_line in text.splitlines():
        if not raw_line:
            lines.append("")
            continue
        lines.extend(textwrap.wrap(raw_line, width=94, replace_whitespace=False) or [""])
    return lines


def build_pdf_bytes(title: str, text: str) -> bytes:
    lines = wrap_pdf_lines(text)
    pages = [lines[i : i + 58] for i in range(0, len(lines), 58)] or [[]]

    objects: list[bytes] = []

    def add(obj: str | bytes) -> int:
        if isinstance(obj, str):
            obj = obj.encode("latin-1", errors="replace")
        objects.append(obj)
        return len(objects)

    catalog_id = add("<< /Type /Catalog /Pages 2 0 R >>")
    pages_id = add(b"")
    font_id = add("<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>")
    page_ids = []

    for page in pages:
        content_lines = ["BT", "/F1 9 Tf", "11 TL", "48 760 Td"]
        for line in page:
            content_lines.append(f"({pdf_escape(line)}) Tj")
            content_lines.append("T*")
        content_lines.append("ET")
        stream = "\n".join(content_lines).encode("latin-1", errors="replace")
        stream_id = add(b"<< /Length " + str(len(stream)).encode() + b" >>\nstream\n" + stream + b"\nendstream")
        page_id = add(
            f"<< /Type /Page /Parent {pages_id} 0 R /MediaBox [0 0 612 792] "
            f"/Resources << /Font << /F1 {font_id} 0 R >> >> /Contents {stream_id} 0 R >>"
        )
        page_ids.append(page_id)

    objects[pages_id - 1] = f"<< /Type /Pages /Kids [{' '.join(f'{pid} 0 R' for pid in page_ids)}] /Count {len(page_ids)} >>".encode()

    output = bytearray(b"%PDF-1.4\n%\xe2\xe3\xcf\xd3\n")
    offsets = [0]
    for idx, obj in enumerate(objects, start=1):
        offsets.append(len(output))
        output.extend(f"{idx} 0 obj\n".encode())
        output.extend(obj)
        output.extend(b"\nendobj\n")
    xref = len(output)
    output.extend(f"xref\n0 {len(objects) + 1}\n".encode())
    output.extend(b"0000000000 65535 f \n")
    for offset in offsets[1:]:
        output.extend(f"{offset:010d} 00000 n \n".encode())
    output.extend(
        f"trailer\n<< /Size {len(objects) + 1} /Root {catalog_id} 0 R /Info << /Title ({pdf_escape(title)}) >> >>\n"
        f"startxref\n{xref}\n%%EOF\n".encode("latin-1", errors="replace")
    )
    return bytes(output)


def file_sha(path: Path) -> str:
    h = sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def write_doc(doc: AgentDoc, formats: set[str]) -> list[dict[str, Any]]:
    written = []
    base_name = slug(doc.document_id)

    renderers = {
        "txt": (".txt", lambda: render_plain(doc).encode("utf-8")),
        "md": (".md", lambda: render_markdown(doc).encode("utf-8")),
        "html": (".html", lambda: render_html(doc).encode("utf-8")),
        "pdf": (".pdf", lambda: build_pdf_bytes(doc.title, render_plain(doc))),
    }

    for fmt in FORMAT_ORDER:
        if fmt not in formats:
            continue
        suffix, renderer = renderers[fmt]
        path = RAW / fmt / AGENT_INPUT_ROOT / doc.category / f"{base_name}{suffix}"
        write_bytes(path, renderer())
        written.append({"format": fmt, "path": rel(path), "bytes": path.stat().st_size, "sha256": file_sha(path)})
    return written


def write_manifest(docs: list[AgentDoc], formats: set[str], file_entries: dict[str, list[dict[str, Any]]]) -> None:
    manifest = {
        "document_count": len(docs),
        "formats": [fmt for fmt in FORMAT_ORDER if fmt in formats],
        "purpose": "Cross-format extraction consistency validation for HLS agent inputs.",
        "generation_policy": {
            "source_truth": "Normalized JSON entities derived from corpus/",
            "no_new_facts": True,
            "image_formats": "Not generated in HLS because public/source documents are digital; scanned patient/lab images would be misleading.",
        },
        "documents": [
            {
                "document_id": doc.document_id,
                "category": doc.category,
                "source_entity_path": doc.source_entity_path,
                "replicas": file_entries[doc.document_id],
            }
            for doc in docs
        ],
    }
    write_text(MANIFEST, json.dumps(manifest, indent=2, sort_keys=True, ensure_ascii=False))


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Generate HLS multi-format agent document replicas.")
    parser.add_argument(
        "--formats",
        nargs="+",
        choices=["txt", "md", "html", "pdf"],
        default=["txt", "md", "html", "pdf"],
        help="Formats to generate.",
    )
    parser.add_argument(
        "--categories",
        nargs="+",
        choices=[
            "all",
            "research_documents",
            "clinical_trials",
            "experimental_datasets",
            "regulatory_submissions",
            "policy_rag",
            "curation_decisions",
            "synthetic_eln_lims",
        ],
        default=["all"],
        help="Entity categories to render.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    formats = set(args.formats)
    categories = set(args.categories)

    if LEGACY_OUT.exists():
        shutil.rmtree(LEGACY_OUT)
    for fmt in FORMAT_ORDER:
        agent_input_dir = RAW / fmt / AGENT_INPUT_ROOT
        if agent_input_dir.exists():
            shutil.rmtree(agent_input_dir)
    if MANIFEST.exists():
        MANIFEST.unlink()

    docs = collect_docs(categories)
    file_entries = {}
    for doc in docs:
        file_entries[doc.document_id] = write_doc(doc, formats)
    write_manifest(docs, formats, file_entries)

    total_files = sum(len(entries) for entries in file_entries.values())
    print(f"\nGenerated {len(docs)} evidence cards ({total_files} files) across {len(formats)} formats.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
