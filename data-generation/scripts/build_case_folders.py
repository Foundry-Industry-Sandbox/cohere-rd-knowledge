#!/usr/bin/env python3
"""
Build dataset-seed/cases/ demo folders from corpus exports.

Each case folder matches the committed demo layout:

    dataset-seed/cases/case-XX_<path>/
      README.md                     (preserved — not overwritten)
      ingest/                       flat Fabric upload payload

    dataset-seed/cases/case-04-demo/
      ingest/                       ING-001 upload files
      prompts/                      QRY-001 and QRY-002 query text

Run AFTER generate_raw_layer.py (and optionally generate_agent_documents.py for ING-004 txt).
"""

from __future__ import annotations

import shutil
from pathlib import Path
from xml.etree import ElementTree as ET
from xml.sax.saxutils import escape

from generate_normalized_layers import (
    CATALOG_PATH,
    RAW,
    build_entity_index,
    load_json,
    normalize_raw_ref,
)
from scenarios import (
    CASE_FOLDERS,
    CASE_QUERY_PROMPTS,
    DEMO_CASE,
    DEMO_PROMPT_FILES,
    SCENARIOS_BY_ID,
)

SCRIPTS = Path(__file__).resolve().parent
DATA_GEN = SCRIPTS.parent
REPO = DATA_GEN.parent
RUNTIME_SEED = REPO / "dataset-seed"
CASES_DIR = RUNTIME_SEED / "cases"

SKIP_INGEST_NAMES = {"europe_pmc_metadata.json", "pmc_oa_license.xml"}
TEXT_INGEST_EXTENSIONS = {".csv", ".html", ".htm", ".json", ".md", ".txt"}
MAX_COMPACT_ABSTRACT_CHARS = 1200
MAX_COMPACT_SECTION_CHARS = 650
MAX_COMPACT_SECTIONS = 6
MAX_COMPACT_REFERENCES = 4
MAX_COMPACT_AUTHORS = 8


def corpus_path(ref: str) -> Path:
    normalized = normalize_raw_ref(ref)
    if normalized.startswith("corpus/"):
        normalized = normalized[len("corpus/") :]
    return RAW / normalized


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def xml_text(elem: ET.Element | None) -> str:
    if elem is None:
        return ""
    return " ".join("".join(elem.itertext()).split())


def compact_text(value: str, limit: int) -> str:
    text = " ".join(value.split())
    return text if len(text) <= limit else text[: limit - 3].rstrip() + "..."


def first_xml(root: ET.Element | None, name: str) -> ET.Element | None:
    if root is None:
        return None
    for elem in root.iter():
        if local_name(elem.tag) == name:
            return elem
    return None


def child_xml(elem: ET.Element | None, name: str) -> ET.Element | None:
    if elem is None:
        return None
    for child in elem:
        if local_name(child.tag) == name:
            return child
    return None


def all_xml(root: ET.Element | None, name: str) -> list[ET.Element]:
    if root is None:
        return []
    return [elem for elem in root.iter() if local_name(elem.tag) == name]


def article_id(root: ET.Element, id_type: str) -> str:
    for elem in all_xml(root, "article-id"):
        if elem.get("pub-id-type") == id_type:
            return xml_text(elem)
    return ""


def article_journal(root: ET.Element) -> str:
    for elem in all_xml(root, "journal-id"):
        if elem.get("journal-id-type") == "iso-abbrev":
            journal = xml_text(elem)
            if journal:
                return journal
    return xml_text(first_xml(root, "journal-title"))


def article_license(root: ET.Element) -> str:
    license_elem = first_xml(root, "license")
    return compact_text(xml_text(license_elem), 220) or "unknown"


def compact_pub_date(root: ET.Element) -> str:
    pub_date = first_xml(root, "pub-date")
    if pub_date is None:
        return ""

    parts: list[str] = []
    for name in ("year", "month", "day"):
        value = xml_text(child_xml(pub_date, name))
        if value:
            parts.append(f"<{name}>{escape(value)}</{name}>")
    return "".join(parts)


def compact_authors(root: ET.Element) -> list[tuple[str, str]]:
    authors: list[tuple[str, str]] = []
    for contrib in all_xml(root, "contrib"):
        contrib_type = contrib.get("contrib-type")
        if contrib_type and contrib_type != "author":
            continue
        if child_xml(contrib, "role") is not None:
            continue
        name = child_xml(contrib, "name")
        surname = xml_text(child_xml(name, "surname"))
        given = xml_text(child_xml(name, "given-names"))
        if surname or given:
            authors.append((surname, given))
        if len(authors) >= MAX_COMPACT_AUTHORS:
            break
    return authors


def compact_keywords(root: ET.Element) -> list[str]:
    keywords: list[str] = []
    for elem in all_xml(root, "kwd"):
        keyword = xml_text(elem)
        if keyword:
            keywords.append(compact_text(keyword, 80))
    return keywords[:12]


def direct_paragraph_text(section: ET.Element) -> str:
    paragraphs = [
        xml_text(child)
        for child in section
        if local_name(child.tag) == "p" and xml_text(child)
    ]
    return " ".join(paragraphs)


def compact_sections(root: ET.Element) -> list[tuple[str, str, str]]:
    body = first_xml(root, "body")
    sections: list[tuple[str, str, str]] = []
    for section in all_xml(body, "sec"):
        title = xml_text(child_xml(section, "title"))
        text = direct_paragraph_text(section)
        if not title or not text:
            continue
        section_id = section.get("id") or f"compact-sec-{len(sections) + 1}"
        sections.append(
            (
                section_id,
                compact_text(title, 120),
                compact_text(text, MAX_COMPACT_SECTION_CHARS),
            )
        )
        if len(sections) >= MAX_COMPACT_SECTIONS:
            break
    return sections


def compact_references(root: ET.Element) -> list[str]:
    references: list[str] = []
    for ref in all_xml(root, "ref"):
        citation = first_xml(ref, "mixed-citation")
        if citation is None:
            citation = first_xml(ref, "element-citation")
        if citation is None:
            citation = ref
        text = compact_text(xml_text(citation), 260)
        if text:
            references.append(text)
        if len(references) >= MAX_COMPACT_REFERENCES:
            break
    return references


def build_compact_jats_xml(src_file: Path, pmcid: str) -> str:
    root = ET.parse(src_file).getroot()
    pmcid_value = article_id(root, "pmcid") or pmcid
    pmid = article_id(root, "pmid")
    doi = article_id(root, "doi")
    title = compact_text(xml_text(first_xml(root, "article-title")), 260)
    journal = compact_text(article_journal(root), 160)
    pub_date = compact_pub_date(root)
    license_text = article_license(root)
    abstract = compact_text(xml_text(first_xml(root, "abstract")), MAX_COMPACT_ABSTRACT_CHARS)
    authors = compact_authors(root)
    keywords = compact_keywords(root)
    sections = compact_sections(root)
    references = compact_references(root)

    author_xml = "\n".join(
        "          <contrib contrib-type=\"author\"><name>"
        f"<surname>{escape(surname)}</surname><given-names>{escape(given)}</given-names>"
        "</name></contrib>"
        for surname, given in authors
    )
    keyword_xml = "".join(f"<kwd>{escape(keyword)}</kwd>" for keyword in keywords)
    section_xml = "\n".join(
        "    <sec id=\"{section_id}\"><title>{title}</title><p>{text}</p></sec>".format(
            section_id=escape(section_id, {"\"": "&quot;"}),
            title=escape(section_title),
            text=escape(section_text),
        )
        for section_id, section_title, section_text in sections
    )
    reference_xml = "\n".join(
        f"      <ref id=\"compact-ref-{index}\"><mixed-citation>{escape(reference)}</mixed-citation></ref>"
        for index, reference in enumerate(references, start=1)
    )

    return f"""<?xml version="1.0" encoding="utf-8"?>
<article article-type="review-article">
  <front>
    <journal-meta>
      <journal-title-group><journal-title>{escape(journal)}</journal-title></journal-title-group>
    </journal-meta>
    <article-meta>
      <article-id pub-id-type="pmcid">{escape(pmcid_value)}</article-id>
      <article-id pub-id-type="pmid">{escape(pmid)}</article-id>
      <article-id pub-id-type="doi">{escape(doi)}</article-id>
      <title-group><article-title>{escape(title)}</article-title></title-group>
      <contrib-group>
{author_xml}
      </contrib-group>
      <pub-date pub-type="epub">{pub_date}</pub-date>
      <permissions><license><license-p>{escape(license_text)}</license-p></license></permissions>
      <abstract><p>{escape(abstract)}</p></abstract>
      <kwd-group>{keyword_xml}</kwd-group>
    </article-meta>
  </front>
  <body>
{section_xml}
  </body>
  <back>
    <ref-list>
{reference_xml}
    </ref-list>
  </back>
</article>
"""


def copy_ingest_file(src_file: Path, dst_root: Path) -> bool:
    rel = src_file.relative_to(RAW)
    if rel.name in SKIP_INGEST_NAMES:
        return False

    if rel.name == "article.xml" and "pmc_oa" in rel.parts:
        pmcid = rel.parts[-2]
        dst_file = dst_root / f"{pmcid}_article.xml"
    else:
        dst_file = dst_root / rel.name

    if dst_file.exists():
        raise FileExistsError(f"flattened ingest filename collision: {dst_file}")

    dst_file.parent.mkdir(parents=True, exist_ok=True)
    if rel.name == "article.xml" and "pmc_oa" in rel.parts:
        dst_file.write_text(build_compact_jats_xml(src_file, rel.parts[-2]), encoding="utf-8")
    elif rel.suffix.lower() in TEXT_INGEST_EXTENSIONS:
        content = src_file.read_text(encoding="utf-8").replace("\r\n", "\n")
        dst_file.write_text("" if content == "" else content.rstrip() + "\n", encoding="utf-8")
    else:
        shutil.copy2(src_file, dst_file)
    return True


def copy_corpus_ref(ref: str, dst_root: Path, warnings: list[str]) -> int:
    src = corpus_path(ref)
    if not src.is_file():
        warnings.append(f"missing corpus file: {ref}")
        return 0
    return 1 if copy_ingest_file(src, dst_root) else 0


def ingestion_stage(scenario: dict) -> dict:
    for stage in scenario["stages"]:
        if stage["stage"] == "ingestion_translation":
            return stage
    raise KeyError(f"no ingestion_translation stage in {scenario['scenario_id']}")


def build_ingest(scenario: dict, entity_index: dict[str, dict], dst: Path) -> tuple[int, list[str]]:
    if dst.exists():
        shutil.rmtree(dst)
    dst.mkdir(parents=True)

    stage = ingestion_stage(scenario)
    warnings: list[str] = []
    count = 0

    for entity_id in stage.get("input_entities_raw", []):
        entity = entity_index.get(entity_id)
        if entity is None:
            warnings.append(f"unresolved entity: {entity_id}")
            continue
        for ref in entity.get("raw_sources", []):
            count += copy_corpus_ref(ref, dst, warnings)

    for ref in stage.get("input_raw", []):
        count += copy_corpus_ref(ref, dst, warnings)

    return count, warnings


def rebuild_ingest(case_name: str, scenario_id: str, entity_index: dict[str, dict]) -> tuple[int, list[str]]:
    scenario = SCENARIOS_BY_ID[scenario_id]
    ingest_dir = CASES_DIR / case_name / "ingest"
    return build_ingest(scenario, entity_index, ingest_dir)


def rebuild_demo_prompts() -> int:
    prompts_dir = CASES_DIR / DEMO_CASE / "prompts"
    if prompts_dir.exists():
        shutil.rmtree(prompts_dir)
    prompts_dir.mkdir(parents=True)
    count = 0
    for scenario_id, filename in DEMO_PROMPT_FILES.items():
        scenario = SCENARIOS_BY_ID[scenario_id]
        for stage in scenario["stages"]:
            if stage.get("prompt"):
                (prompts_dir / filename).write_text(stage["prompt"].strip() + "\n", encoding="utf-8")
                count += 1
                break
    return count


def rebuild_query_case_prompts(scenario_id: str, case_name: str) -> int:
    filenames = CASE_QUERY_PROMPTS[scenario_id]
    scenario = SCENARIOS_BY_ID[scenario_id]
    prompt_stages = [s for s in scenario["stages"] if s.get("prompt")]
    if len(filenames) != len(prompt_stages):
        raise ValueError(
            f"{scenario_id}: expected {len(filenames)} prompt files, found {len(prompt_stages)} prompt stages"
        )

    prompts_dir = CASES_DIR / case_name / "prompts"
    if prompts_dir.exists():
        shutil.rmtree(prompts_dir)
    prompts_dir.mkdir(parents=True)

    count = 0
    for filename, stage in zip(filenames, prompt_stages):
        (prompts_dir / filename).write_text(stage["prompt"].strip() + "\n", encoding="utf-8")
        count += 1
    return count


def main() -> None:
    if not RAW.is_dir():
        raise RuntimeError("Corpus is missing. Run generate_raw_layer.py first.")

    catalog = load_json(CATALOG_PATH)
    entity_index = build_entity_index(catalog)
    all_warnings: list[str] = []
    ingest_total = 0

    for scenario_id, case_name in CASE_FOLDERS.items():
        if SCENARIOS_BY_ID[scenario_id]["flow"] != "ingestion":
            continue
        count, warnings = rebuild_ingest(case_name, scenario_id, entity_index)
        rel = f"cases/{case_name}"
        flag = f"  WARN {len(warnings)}" if warnings else ""
        print(f"{rel}/ingest: {count} files{flag}")
        ingest_total += count
        all_warnings.extend(f"{scenario_id}: {w}" for w in warnings)

    count, warnings = rebuild_ingest(DEMO_CASE, "ING-001", entity_index)
    print(f"cases/{DEMO_CASE}/ingest: {count} files")
    ingest_total += count
    all_warnings.extend(f"ING-001: {w}" for w in warnings)

    prompt_count = rebuild_demo_prompts()
    print(f"cases/{DEMO_CASE}/prompts: {prompt_count} files")

    for scenario_id in CASE_QUERY_PROMPTS:
        case_name = CASE_FOLDERS[scenario_id]
        q_count = rebuild_query_case_prompts(scenario_id, case_name)
        print(f"cases/{case_name}/prompts: {q_count} files")
        prompt_count += q_count

    print(
        f"\nDone — {ingest_total} ingest files and {prompt_count} prompts "
        f"under {CASES_DIR.relative_to(REPO)}/"
    )
    if all_warnings:
        print("\nWarnings:")
        for w in all_warnings:
            print(f"  - {w}")


if __name__ == "__main__":
    main()
