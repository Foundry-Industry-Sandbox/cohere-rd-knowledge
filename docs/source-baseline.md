# Source Baseline - HLS Agentic R&D Knowledge Mining

This document defines the public-source baseline for the first dataset iteration. It is not the Raw Layer yet. The Raw Layer will be created later from this baseline.

## Baseline Decision

Use an oncology R&D knowledge-mining scenario centered on **EGFR-mutated non-small cell lung cancer (NSCLC)** and **osimertinib / Tagrisso / AZD9291**.

This scope gives us a coherent public evidence graph across:

- research articles and reviews,
- clinical trial protocols and posted results,
- experimental cell-line datasets,
- compound and target registries,
- FDA and EMA regulatory documents,
- clinical trial policy and transparency rules,
- simulated ELN/LIMS records derived from non-patient public experimental structures.

## Compliance Boundary

The baseline must not use patient-level clinical records, patient names, masked patient identities, real-world patient trajectories, or case-report narratives as source content.

Allowed source types:

- aggregate clinical trial registration and results data,
- public protocol and statistical-analysis-plan documents,
- open-access research articles with explicit reuse terms,
- cell-line or assay-level experimental datasets,
- public product labels and regulator assessment pages,
- regional policy and transparency guidance.

Excluded source types:

- patient-level datasets, even when deidentified,
- case reports or single-patient narratives,
- real-world cohort records with sensitive demographic slices unless used only as a discarded-source example,
- GEO records whose samples are labeled as real patient specimens,
- articles in PMC OA with unclear license if full text reuse would be needed.

If a future workflow needs people, reviewers, scientists, lab staff, or decision makers, those names must be fictional and created only in the simulated Raw Layer.

## Public Source Set

| Source family | Selected use | Baseline source | Use mode | PHI/PII posture |
| --- | --- | --- | --- | --- |
| Research articles | Scientific background, resistance mechanisms, treatment landscape | PMC Open Access / Europe PMC | Direct for licensed OA articles; metadata-only otherwise | No patient narratives selected |
| Clinical trials | Protocols, SAPs, arms, outcomes, posted results, sponsor/collaborator metadata | ClinicalTrials.gov API / AACT | Direct | Aggregate records only |
| Experimental datasets | Cell-line assays, RNA-seq, CRISPR screens, resistance/sensitivity models | NCBI GEO | Direct for cell-line datasets | No patient-sample GEO records selected |
| Study packages / supplementary records | Optional supplementary packages when aligned | EMBL-EBI BioStudies | Reference/optional | Use only if a clean accession is linked later |
| Compound and target registry | Compound aliases, mechanism, target, ATC class | ChEMBL | Direct metadata | No patient data |
| US regulatory | Label, application number, warnings, indications, approval evidence | openFDA / Drugs@FDA | Direct metadata; Raw PDFs later if needed | openFDA states it serves public data without patient PII/sensitive data |
| EU regulatory | Product information, EPAR overview, risk-management plan, assessment history | EMA Tagrisso EPAR | Direct | Public regulatory documents |
| Region policies | Trial conduct, GCP, transparency, personal-data and CCI boundaries | FDA and EU clinical trial policy pages | Direct | Policy text only |

## Canonical Entity Anchors

| Entity type | Canonical value | Public identifiers / aliases | Source |
| --- | --- | --- | --- |
| Compound | Osimertinib | Tagrisso, AZD9291, CHEMBL3353410, ATC L01EB04, FDA NDA208065 | ChEMBL, openFDA, EMA |
| Target | EGFR / ERBB1 | CHEMBL203 | ChEMBL |
| Disease area | EGFR-mutated NSCLC | NSCLC, non-small cell lung cancer, EGFRm+ NSCLC | ClinicalTrials.gov, EMA |
| Biomarkers | EGFR Ex19del, EGFR L858R, EGFR T790M, EGFR C797S | sensitising mutations, resistance mutations | ClinicalTrials.gov, PMC OA |
| Sponsor | AstraZeneca | lead sponsor across selected trials | ClinicalTrials.gov |
| Trial collaborator | Parexel | collaborator for FLAURA | ClinicalTrials.gov |
| Regulators | FDA, EMA, European Commission | Drugs@FDA, openFDA, EPAR, EU CTR | FDA, EMA, EC |
| Experimental models | PC9, H1650, PC9OR, PC9 sensitive/resistant | GEO sample model names only | GEO |

## Clinical Trial Baseline

These trials form the clinical evidence spine. All selected records are public, aggregate, and include posted results. Each also has large-document entries for a study protocol and statistical analysis plan.

| Trial | NCT ID | Role in baseline | Public status | Enrollment | Key linkage |
| --- | --- | --- | --- | ---: | --- |
| FLAURA | [NCT02296125](https://clinicaltrials.gov/study/NCT02296125) | First-line osimertinib versus erlotinib/gefitinib in EGFRm+ locally advanced/metastatic NSCLC | Completed, results posted | 674 | Core trial for protocol, SAP, PFS/ORR/OS, sponsor/collaborator metadata |
| AURA3 | [NCT02151981](https://clinicaltrials.gov/study/NCT02151981) | Osimertinib versus platinum-based chemotherapy after EGFR TKI progression in T790M-positive NSCLC | Completed, results posted | 421 | Resistance-mutation clinical evidence |
| ADAURA | [NCT02511106](https://clinicaltrials.gov/study/NCT02511106) | Adjuvant osimertinib after complete tumor resection | Active, not recruiting; results posted | 682 | Adjuvant indication evidence |
| FLAURA2 | [NCT04035486](https://clinicaltrials.gov/study/NCT04035486) | Osimertinib with or without chemotherapy as first-line treatment | Active, not recruiting; results posted | 587 | Combination-therapy and label-update evidence |

## Research Article Baseline

The Raw Layer should use full text only for articles with explicit license records that allow reuse. These articles are candidates for the first article set.

| PMCID | Article | Journal / date | License posture | Baseline use |
| --- | --- | --- | --- | --- |
| [PMC6889286](https://pmc.ncbi.nlm.nih.gov/articles/PMC6889286/) | Resistance mechanisms to osimertinib in EGFR-mutated non-small cell lung cancer | British Journal of Cancer, 2019 | PMC OA API reports CC BY | Core resistance-mechanism article |
| [PMC5447962](https://pmc.ncbi.nlm.nih.gov/articles/PMC5447962/) | Epidermal Growth Factor Receptor Cell Proliferation Signaling Pathways | Cancers, 2017 | PMC OA API reports CC BY | EGFR biology and pathway grounding |
| [PMC13070087](https://pmc.ncbi.nlm.nih.gov/articles/PMC13070087/) | Updates in the Management of EGFR-Mutated Non-small Cell Lung Cancer (NSCLC) | Current Oncology Reports, 2026 | PMC OA API reports CC BY | Current treatment landscape |
| [PMC13129538](https://pmc.ncbi.nlm.nih.gov/articles/PMC13129538/) | The evolving landscape of first-line and subsequent therapies in EGFR-mutated NSCLC | Exploration of Targeted Anti-tumor Therapy, 2026 | PMC OA API reports CC BY | Therapy sequencing and resistance/tolerability |
| [PMC13143971](https://pmc.ncbi.nlm.nih.gov/articles/PMC13143971/) | Non-small cell lung cancer after EGFR-TKI resistance: from drug resistance mechanisms to precision interventions | Frontiers in Pharmacology, 2026 | PMC OA API reports CC BY | Post-TKI resistance and precision interventions |

Candidate to avoid for full-text reuse:

- [PMC4771182](https://pmc.ncbi.nlm.nih.gov/articles/PMC4771182/) is scientifically relevant to C797S resistance, but PMC OA API reports `license="none"`. Use only as metadata/reference unless licensing is manually cleared.

## Experimental Dataset Baseline

Use cell-line datasets instead of patient-sample datasets. This keeps the R&D signal strong while avoiding patient-level privacy concerns.

| GEO accession | Title / focus | Selected reason | PHI posture |
| --- | --- | --- | --- |
| [GSE323366](https://www.ncbi.nlm.nih.gov/geo/query/acc.cgi?acc=GSE323366) | Genome-wide profiling identifies genetic dependencies of cell death following EGFR inhibition - RNA-seq | PC9/H1650 cells with DMSO, erlotinib, and osimertinib conditions | Cell lines only |
| [GSE323365](https://www.ncbi.nlm.nih.gov/geo/query/acc.cgi?acc=GSE323365) | Genome-wide profiling identifies genetic dependencies of cell death following EGFR inhibition | PC9-Cas9 CRISPR-style dependency screen with untreated, erlotinib, and osimertinib arms | Cell lines only |
| [GSE272182](https://www.ncbi.nlm.nih.gov/geo/query/acc.cgi?acc=GSE272182) | N6-methyladenosine modification of cLMNB1 overcomes osimertinib resistance by destabilizing FGFR4 in NSCLC | PC9 sensitive versus resistant replicates | Cell lines only |
| [GSE300311](https://www.ncbi.nlm.nih.gov/geo/query/acc.cgi?acc=GSE300311) | PKM2-deficiency induced fatty acid biosynthesis activation limits the response of NSCLC to osimertinib | PC9 control/PKM2-deficient samples with vehicle/osimertinib and chromatin marks | Cell lines only |
| [GSE298111](https://www.ncbi.nlm.nih.gov/geo/query/acc.cgi?acc=GSE298111) | GLMP promotes EGFR-TKI resistance by activating autophagy and RhoA pathway in NSCLC | PC9OR control versus si-GLMP replicates | Cell lines only |

Explicitly excluded GEO candidates:

- `GSE297057` because sample titles reference patient FFPE specimens.
- `GSE301973` because sample titles refer to before/after treatment specimens.

These records may remain scientifically useful as background, but they should not seed the first Raw Layer.

## Regulatory and Policy Baseline

| Source | Link | Baseline use |
| --- | --- | --- |
| openFDA drug label API | [openFDA drug label docs](https://open.fda.gov/apis/drug/label/) | Label-derived indication, warnings, manufacturer, application number `NDA208065` |
| Drugs@FDA data files | [Drugs@FDA data files](https://www.fda.gov/drugs/drug-approvals-and-databases/drugsfda-data-files) | Application/submission/document metadata model for future regulatory raw files |
| EMA Tagrisso EPAR | [Tagrisso EPAR](https://www.ema.europa.eu/en/medicines/human/EPAR/tagrisso) | EU product information, EPAR overview, risk-management plan, assessment history |
| FDA clinical trials and human subject protection | [FDA GCP/HSP](https://www.fda.gov/science-research/science-and-research-special-topics/clinical-trials-and-human-subject-protection) | US GCP, trial oversight, conduct, reporting policy |
| EU Clinical Trials Regulation | [Regulation EU No 536/2014](https://health.ec.europa.eu/medicinal-products/clinical-trials/clinical-trials-regulation-eu-no-5362014_en) | EU transparency, personal-data and commercially confidential information boundaries |
| ClinicalTrials.gov / AACT | [ClinicalTrials.gov API](https://clinicaltrials.gov/data-api/api), [AACT](https://aact.ctti-clinicaltrials.org/) | Trial registration/results source and normalized relational reference |

## Synthetic ELN/LIMS Direction

Real ELN/LIMS records are not selected as public source material. The Raw Layer should simulate ELN/LIMS records from the selected cell-line datasets and protocols.

Synthetic records should remain coherent with the public sources:

- cell line names: PC9, H1650, PC9OR,
- treatment conditions: DMSO/vehicle, erlotinib, osimertinib,
- resistance labels: sensitive, resistant, osimertinib-resistant,
- assay families: RNA-seq, CRISPR dependency screen, gene knockdown, pathway assay,
- target/biomarker vocabulary: EGFR, FGFR4, PKM2, GLMP, C797S, T790M, Ex19del, L858R.

Synthetic records must not claim to be original public records. They should carry explicit provenance such as `synthetic_from_public_structure`.

## Implemented Source-to-Raw Mapping

The Raw Layer is implemented under `data-generation/corpus/`.

```text
data-generation/
  corpus/
    source_catalog.json
    xml/articles/pmc_oa/...
    json/trials/...
    json/datasets/geo/...
    csv|txt/synthetic_eln_lims/...
    raw_manifest.json
  scripts/
    generate_raw_layer.py
  docs/
    RAW_LAYER.md
```

The Raw Layer should preserve source identifiers in file names and metadata so downstream entities can trace evidence back to source:

- `NCT02296125`
- `NCT02151981`
- `NCT02511106`
- `NCT04035486`
- `PMC6889286`
- `PMC5447962`
- `PMC13070087`
- `PMC13129538`
- `PMC13143971`
- `GSE323366`
- `GSE323365`
- `GSE272182`
- `GSE300311`
- `GSE298111`
- `CHEMBL3353410`
- `NDA208065`

## Implemented Downstream Entity Families

The `dataset-seed` entity layers derived from the Raw Layer are:

Ground-truth rollups (optional validation) live in `data-generation/ground-truth/` (`ING-*.json`, `QRY-*.json`) with `SCHEMA.md`. Normalized entities are built in memory during generation — not written to numbered catalog folders.

## Baseline Quality Checks

Before creating Raw files:

- confirm each PMC article is in PMC OA and has a usable license at fetch time,
- fetch ClinicalTrials.gov records through the official API or AACT, not scraped pages,
- use only aggregate trial results and protocol/SAP documents,
- use only GEO records whose samples are cell-line or assay-level,
- do not import real patient IDs, patient names, dates of service, medical-record IDs, or real-world patient trajectories,
- mark all ELN/LIMS content as synthetic,
- keep every generated downstream entity traceable to at least one source identifier.
