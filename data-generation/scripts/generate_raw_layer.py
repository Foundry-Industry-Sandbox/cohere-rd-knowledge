#!/usr/bin/env python3
"""
Generate the Raw layer for the HLS agentic R&D knowledge-mining dataset.

The raw layer intentionally mixes real public sources and clearly marked
synthetic operational records:

  - Real: PMC OA XML, ClinicalTrials.gov JSON/PDFs, GEO metadata, ChEMBL,
    openFDA, Drugs@FDA, EMA, FDA/EU policy pages.
  - Synthetic: ELN/LIMS-style records and partner repository intake logs derived
    from the public source structure. These records contain no patient data.

Running the script is idempotent: it refreshes data-generation/corpus from the
catalog in data-generation/corpus/source_catalog.json.
"""

from __future__ import annotations

import csv
import hashlib
import json
import re
import shutil
import ssl
import subprocess
import sys
import time
from pathlib import Path
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.parse import quote, urlparse
from urllib.request import Request, urlopen


SCRIPTS = Path(__file__).resolve().parent
DATA_GEN = SCRIPTS.parent
CATALOG_PATH = DATA_GEN / "corpus" / "source_catalog.json"
# Canonical corpus — single source of truth for fetched/synthesized files.
RAW = DATA_GEN / "corpus"

USER_AGENT = (
    "hls-agentic-rd-knowledge-mining/0.1 "
    "(public-source dataset seed; contact: southworks)"
)


def load_catalog() -> dict[str, Any]:
    return json.loads(CATALOG_PATH.read_text(encoding="utf-8"))


def reset_raw() -> None:
    catalog_backup = CATALOG_PATH.read_bytes() if CATALOG_PATH.is_file() else None
    if RAW.exists():
        shutil.rmtree(RAW)
    RAW.mkdir(parents=True, exist_ok=True)
    if catalog_backup is not None:
        CATALOG_PATH.write_bytes(catalog_backup)


def request_bytes(url: str, retries: int = 3) -> bytes:
    req = Request(url, headers={"User-Agent": USER_AGENT})
    last_error: Exception | None = None

    for attempt in range(1, retries + 1):
        try:
            with urlopen(req, timeout=90) as response:
                return response.read()
        except HTTPError as exc:
            return request_bytes_with_curl(url, exc)
        except URLError as exc:
            last_error = exc
            # Some FDA pages can fail certificate validation in local Python
            # installations while curl/browser access succeeds. Fall back only
            # for public-source download continuity.
            reason = str(getattr(exc, "reason", exc))
            if "CERTIFICATE_VERIFY_FAILED" in reason:
                try:
                    context = ssl._create_unverified_context()
                    with urlopen(req, timeout=90, context=context) as response:
                        return response.read()
                except Exception as fallback_exc:
                    return request_bytes_with_curl(url, fallback_exc)
            if attempt < retries:
                time.sleep(1.5 * attempt)
                continue
            raise

    raise RuntimeError(f"Failed to fetch {url}: {last_error}")


def request_bytes_with_curl(url: str, original_error: Exception) -> bytes:
    result = subprocess.run(
        ["curl", "-L", "--fail", "-sS", "-A", USER_AGENT, url],
        check=False,
        capture_output=True,
    )
    if result.returncode != 0:
        stderr = result.stderr.decode("utf-8", errors="replace").strip()
        raise RuntimeError(f"Failed to fetch {url}: {original_error}; curl: {stderr}")
    return result.stdout


def request_text(url: str) -> str:
    return request_bytes(url).decode("utf-8", errors="replace")


def request_json(url: str) -> Any:
    return json.loads(request_text(url))


def write_bytes(path: Path, content: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(content)
    print(f"  {path.relative_to(BASE)} ({len(content):,} bytes)")


def write_text(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content.rstrip() + "\n", encoding="utf-8")
    print(f"  {path.relative_to(BASE)}")


def write_json(path: Path, content: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(content, indent=2, sort_keys=True, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"  {path.relative_to(BASE)}")


def write_csv(path: Path, header: list[str], rows: list[list[Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(header)
        writer.writerows(rows)
    print(f"  {path.relative_to(BASE)} ({len(rows)} rows)")


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def sanitize(value: str) -> str:
    return re.sub(r"[^A-Za-z0-9_.-]+", "_", value).strip("_")


def raw_path(fmt: str, *parts: str | Path) -> Path:
    path = RAW / fmt
    for part in parts:
        path /= part
    return path


def rel(path: Path) -> str:
    return str(path.relative_to(BASE))


def extract_license(oa_xml: str) -> str:
    match = re.search(r'license="([^"]+)"', oa_xml)
    return match.group(1) if match else "unknown"


def download_articles(catalog: dict[str, Any]) -> None:
    print("\nArticles - PMC Open Access")
    for article in catalog["pmc_open_access_articles"]:
        pmcid = article["pmcid"]
        article_json_dir = raw_path("json", "articles", "pmc_oa", pmcid)
        article_xml_dir = raw_path("xml", "articles", "pmc_oa", pmcid)

        metadata_url = (
            "https://www.ebi.ac.uk/europepmc/webservices/rest/search"
            f"?query=PMCID:{pmcid}&format=json&pageSize=1"
        )
        oa_url = f"https://www.ncbi.nlm.nih.gov/pmc/utils/oa/oa.fcgi?id={pmcid}"
        xml_url = f"https://www.ebi.ac.uk/europepmc/webservices/rest/{pmcid}/fullTextXML"

        metadata = request_json(metadata_url)
        oa_xml = request_text(oa_url)
        license_name = extract_license(oa_xml)

        if article["expected_license"].lower() not in license_name.lower():
            raise RuntimeError(
                f"{pmcid} license mismatch: expected {article['expected_license']}, got {license_name}"
            )

        write_json(article_json_dir / "europe_pmc_metadata.json", metadata)
        write_text(article_xml_dir / "pmc_oa_license.xml", oa_xml)
        write_bytes(article_xml_dir / "article.xml", request_bytes(xml_url))
        write_json(
            article_json_dir / "source_record.json",
            {
                "pmcid": pmcid,
                "title": article["title"],
                "license": license_name,
                "metadata_url": metadata_url,
                "oa_license_url": oa_url,
                "full_text_xml_url": xml_url,
                "local_paths": [
                    rel(article_xml_dir / "article.xml"),
                    rel(article_xml_dir / "pmc_oa_license.xml"),
                    rel(article_json_dir / "europe_pmc_metadata.json"),
                ],
                "raw_layer_policy": "Full text allowed only because PMC OA API license matched expected license.",
            },
        )


def clinicaltrials_pdf_url(nct_id: str, filename: str) -> str:
    suffix = nct_id[-2:]
    return f"https://cdn.clinicaltrials.gov/large-docs/{suffix}/{nct_id}/{filename}"


def download_trials(catalog: dict[str, Any]) -> None:
    print("\nClinical trials - ClinicalTrials.gov")
    for trial in catalog["clinical_trials"]:
        nct_id = trial["nct_id"]
        trial_json_dir = raw_path("json", "trials", "clinicaltrials_gov", nct_id)
        trial_pdf_dir = raw_path("pdf", "trials", "clinicaltrials_gov", nct_id)
        study_url = f"https://clinicaltrials.gov/api/v2/studies/{nct_id}"
        study = request_json(study_url)

        write_json(trial_json_dir / "study.json", study)

        docs = (
            study.get("documentSection", {})
            .get("largeDocumentModule", {})
            .get("largeDocs", [])
        )
        doc_manifest = []
        for doc in docs:
            filename = doc["filename"]
            url = clinicaltrials_pdf_url(nct_id, filename)
            local_path = trial_pdf_dir / filename
            write_bytes(local_path, request_bytes(url))
            doc_manifest.append(
                {
                    "label": doc.get("label"),
                    "date": doc.get("date"),
                    "filename": filename,
                    "url": url,
                    "size_from_api": doc.get("size"),
                    "local_path": rel(local_path),
                }
            )

        write_json(
            trial_json_dir / "source_record.json",
            {
                "nct_id": nct_id,
                "acronym": trial["acronym"],
                "role": trial["role"],
                "study_url": study_url,
                "public_page": f"https://clinicaltrials.gov/study/{nct_id}",
                "local_paths": [rel(trial_json_dir / "study.json")] + [d["local_path"] for d in doc_manifest],
                "documents": doc_manifest,
                "raw_layer_policy": "Aggregate trial registration/results plus public protocol/SAP documents only.",
            },
        )


def download_geo(catalog: dict[str, Any]) -> None:
    print("\nExperimental datasets - NCBI GEO")
    for dataset in catalog["geo_cell_line_datasets"]:
        accession = dataset["accession"]
        ds_json_dir = raw_path("json", "datasets", "geo", accession)
        ds_txt_dir = raw_path("txt", "datasets", "geo", accession)

        search_url = (
            "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/esearch.fcgi"
            f"?db=gds&term={quote(accession + '[ACCN]')}&retmode=json&retmax=1"
        )
        search = request_json(search_url)
        id_list = search.get("esearchresult", {}).get("idlist", [])
        if not id_list:
            raise RuntimeError(f"No GEO id found for {accession}")

        uid = id_list[0]
        summary_url = (
            "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/esummary.fcgi"
            f"?db=gds&id={uid}&retmode=json"
        )
        soft_url = (
            "https://www.ncbi.nlm.nih.gov/geo/query/acc.cgi"
            f"?acc={accession}&targ=self&form=text&view=quick"
        )

        summary = request_json(summary_url)
        soft_text = request_text(soft_url)

        write_json(ds_json_dir / "geo_esearch.json", search)
        write_json(ds_json_dir / "geo_esummary.json", summary)
        write_text(ds_txt_dir / "series_soft.txt", soft_text)
        write_json(
            ds_json_dir / "source_record.json",
            {
                "accession": accession,
                "role": dataset["role"],
                "esearch_url": search_url,
                "esummary_url": summary_url,
                "soft_url": soft_url,
                "public_page": f"https://www.ncbi.nlm.nih.gov/geo/query/acc.cgi?acc={accession}",
                "local_paths": [
                    rel(ds_json_dir / "geo_esearch.json"),
                    rel(ds_json_dir / "geo_esummary.json"),
                    rel(ds_txt_dir / "series_soft.txt"),
                ],
                "raw_layer_policy": "Selected because samples are cell-line or assay-level records, not patient specimens.",
            },
        )


def download_chembl(catalog: dict[str, Any]) -> None:
    print("\nCompound and target registry - ChEMBL")
    compound = catalog["compound_anchor"]
    chembl_id = compound["chembl_id"]
    out_dir = raw_path("json", "registries", "chembl", chembl_id)

    urls = {
        "molecule.json": f"https://www.ebi.ac.uk/chembl/api/data/molecule/{chembl_id}.json",
        "mechanism.json": f"https://www.ebi.ac.uk/chembl/api/data/mechanism.json?molecule_chembl_id={chembl_id}",
        "target_CHEMBL203.json": "https://www.ebi.ac.uk/chembl/api/data/target/CHEMBL203.json",
    }

    for filename, url in urls.items():
        write_json(out_dir / filename, request_json(url))

    write_json(
        out_dir / "source_record.json",
        {
            "chembl_id": chembl_id,
            "preferred_name": compound["preferred_name"],
            "aliases": compound["aliases"],
            "urls": urls,
            "local_paths": [rel(out_dir / filename) for filename in urls],
            "raw_layer_policy": "Public compound/target metadata only.",
        },
    )


def extension_for_url(url: str, fallback: str = ".html") -> str:
    path = urlparse(url).path
    suffix = Path(path).suffix.lower()
    return suffix if suffix in {".json", ".pdf", ".html", ".htm"} else fallback


def download_named_sources(items: list[dict[str, str]], raw_subdir: str, label: str) -> None:
    print(f"\n{label}")
    for item in items:
        source_id = item["source_id"]
        url = item["url"]
        ext = extension_for_url(url, ".html")
        fmt = "html" if ext == ".htm" else ext.lstrip(".")
        stored_ext = ".html" if fmt == "html" else ext
        out_dir = raw_path(fmt, raw_subdir, sanitize(source_id))
        record_dir = raw_path("json", raw_subdir, sanitize(source_id))
        local_path = out_dir / f"{sanitize(source_id)}{stored_ext}"

        if ext == ".json":
            write_json(local_path, request_json(url))
        elif ext == ".pdf":
            write_bytes(local_path, request_bytes(url))
        else:
            write_text(local_path, request_text(url))

        write_json(
            record_dir / "source_record.json",
            {
                "source_id": source_id,
                "url": url,
                "format": fmt,
                "local_path": rel(local_path),
                "raw_layer_policy": "Public regulatory or policy source page/document.",
            },
        )


def build_synthetic_eln_lims() -> None:
    print("\nSynthetic ELN/LIMS and partner repository records")
    txt_dir = raw_path("txt", "synthetic_eln_lims")
    csv_dir = raw_path("csv", "synthetic_eln_lims")

    sample_rows = [
        ["SYN-LIMS-001", "GSE323366", "GSM9564408", "PC9", "DMSO", "0 hour", 1, "RNA-seq", "baseline control"],
        ["SYN-LIMS-002", "GSE323366", "GSM9564414", "PC9", "DMSO", "0 hour", 2, "RNA-seq", "baseline control"],
        ["SYN-LIMS-003", "GSE323366", "GSM9564412", "PC9", "osimertinib 0.1 uM", "36 hours", 1, "RNA-seq", "EGFR inhibition response"],
        ["SYN-LIMS-004", "GSE323366", "GSM9564418", "PC9", "osimertinib 0.1 uM", "36 hours", 2, "RNA-seq", "EGFR inhibition response"],
        ["SYN-LIMS-005", "GSE323366", "GSM9564413", "H1650", "osimertinib 0.1 uM", "36 hours", 1, "RNA-seq", "EGFR inhibition response"],
        ["SYN-LIMS-006", "GSE323365", "GSM9564404", "PC9-Cas9", "osimertinib 0.1 uM", "endpoint", 1, "CRISPR dependency screen", "drug lethality dependency"],
        ["SYN-LIMS-007", "GSE272182", "GSM8395370", "PC9", "sensitive", "endpoint", 1, "RNA-seq", "osimertinib-sensitive model"],
        ["SYN-LIMS-008", "GSE272182", "GSM8395373", "PC9", "resistant", "endpoint", 1, "RNA-seq", "osimertinib-resistant model"],
        ["SYN-LIMS-009", "GSE300311", "GSM9058173", "PC9 sgCtrl", "osimertinib", "endpoint", 1, "chromatin/profile assay", "control under treatment"],
        ["SYN-LIMS-010", "GSE298111", "GSM9008621", "PC9OR si-GLMP", "si-GLMP", "endpoint", 1, "RNA-seq", "resistance pathway knockdown"],
    ]
    write_csv(
        csv_dir / "lims_sample_manifest.csv",
        [
            "SAMPLE_ID",
            "SOURCE_GEO_SERIES",
            "SOURCE_GEO_SAMPLE",
            "MODEL",
            "CONDITION",
            "TIMEPOINT",
            "REPLICATE",
            "ASSAY",
            "INTENDED_SIGNAL",
        ],
        sample_rows,
    )

    write_text(
        txt_dir / "eln_experiment_notebook.txt",
        """
SYNTHETIC ELN NOTEBOOK EXPORT
System: HelixBench ELN (synthetic)
Project: EGFRm NSCLC osimertinib knowledge-mining baseline
Provenance: synthetic_from_public_structure

IMPORTANT: This file is generated for the dataset seed. It does not contain
real patient data, real patient names, or real clinical trajectories.

Entry ELN-OSM-001
Date: 2026-02-03
Scientist: Dr. Elena Mora (fictional)
Objective: Align public PC9/H1650 EGFR-inhibition RNA-seq source records to
the research knowledge hub ingestion model.
Linked public sources: GSE323366, PMC6889286, CHEMBL3353410.
Materials: PC9 and H1650 cell-line metadata; DMSO, erlotinib, osimertinib.
Observation: Osimertinib-treated PC9 and H1650 samples should be linked to
EGFR inhibition, resistance-mechanism literature, and FLAURA/AURA3 trial context.

Entry ELN-OSM-002
Date: 2026-02-10
Scientist: Dr. Noah Ibarra (fictional)
Objective: Prepare resistant/sensitive model mapping for downstream entity
extraction.
Linked public sources: GSE272182, GSE298111, PMC13143971.
Materials: PC9 sensitive, PC9 resistant, PC9OR si-GLMP metadata.
Observation: Resistance-linked records should preserve the distinction between
cell-line models and clinical trial populations.

Entry ELN-OSM-003
Date: 2026-02-17
Scientist: Dr. Priya Nandakumar (fictional)
Objective: Curate compliance notes for public regulatory and regional policy
documents.
Linked public sources: NDA208065, EMA Tagrisso EPAR, FDA clinical trial policy,
EU Clinical Trials Regulation 536/2014.
Observation: Clinical trial results are aggregate. Any generated reviewer or
owner names are fictional. No patient identifiers are used.
""",
    )

    write_text(
        txt_dir / "lims_quality_control_report.txt",
        """
SYNTHETIC LIMS QUALITY CONTROL REPORT
System: HelixBench LIMS (synthetic)
Report ID: QC-OSM-RAW-001
Provenance: synthetic_from_public_structure

Scope:
- Confirm selected GEO records are cell-line or assay-level records.
- Confirm selected ClinicalTrials.gov records are aggregate protocol/results records.
- Confirm article full text is restricted to PMC OA records with expected license.
- Confirm no patient-level records, case reports, or before/after patient specimens
  are selected for the Raw Layer.

Result:
PASS

Reviewer:
Marisol Vega, Research Data Steward (fictional)
""",
    )


def build_partner_vendor_index(catalog: dict[str, Any]) -> None:
    rows = [
        ["PVR-001", "ClinicalTrials.gov", "trial registry/results", "NCT02296125;NCT02151981;NCT02511106;NCT04035486", "public aggregate records"],
        ["PVR-002", "NCBI PMC Open Access", "research articles", ";".join(a["pmcid"] for a in catalog["pmc_open_access_articles"]), "license-filtered OA full text"],
        ["PVR-003", "NCBI GEO", "experimental datasets", ";".join(d["accession"] for d in catalog["geo_cell_line_datasets"]), "cell-line datasets only"],
        ["PVR-004", "ChEMBL", "compound and target registry", catalog["compound_anchor"]["chembl_id"], "public compound metadata"],
        ["PVR-005", "openFDA / Drugs@FDA", "US regulatory submissions and label", catalog["compound_anchor"]["fda_application_number"], "public regulatory data"],
        ["PVR-006", "EMA", "EU regulatory EPAR", "Tagrisso EPAR", "public regulatory documents"],
    ]
    write_csv(
        raw_path("csv", "partner_vendor_repositories", "partner_vendor_repository_index.csv"),
        ["REPOSITORY_ID", "REPOSITORY_NAME", "SOURCE_TYPE", "SOURCE_KEYS", "ACCESS_POLICY"],
        rows,
    )


def write_raw_manifest(catalog: dict[str, Any]) -> None:
    files = []
    for path in sorted(RAW.rglob("*")):
        if path.is_file() and path.name != "raw_manifest.json":
            files.append(
                {
                    "path": str(path.relative_to(BASE)),
                    "bytes": path.stat().st_size,
                    "sha256": sha256(path),
                }
            )

    write_json(
        RAW / "raw_manifest.json",
        {
            "scenario": catalog["scenario"],
            "therapeutic_area": catalog["therapeutic_area"],
            "compound_anchor": catalog["compound_anchor"],
            "raw_file_count": len(files),
            "raw_files": files,
            "privacy_posture": {
                "patient_level_data": "excluded",
                "patient_names": "excluded",
                "synthetic_people": "fictional lab/reviewer names only",
                "selected_geo_records": "cell-line or assay-level records only",
            },
        },
    )


def main() -> int:
    catalog = load_catalog()
    reset_raw()

    download_articles(catalog)
    download_trials(catalog)
    download_geo(catalog)
    download_chembl(catalog)
    download_named_sources(catalog["regulatory_sources"], "regulatory", "Regulatory sources")
    download_named_sources(catalog["policy_sources"], "policies", "Policy sources")
    build_synthetic_eln_lims()
    build_partner_vendor_index(catalog)
    write_raw_manifest(catalog)

    print("\nRaw layer generated successfully.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
