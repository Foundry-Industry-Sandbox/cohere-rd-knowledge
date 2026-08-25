# Format Decisions

## Corpus layout

Raw files are organized by format under `corpus/{xml,json,pdf,html,txt,csv,md}/`.

## Agent input replicas

Cross-format evidence cards live under:

```
corpus/txt/agent_inputs/<category>/<document_id>.txt
corpus/md/agent_inputs/<category>/<document_id>.md
corpus/html/agent_inputs/<category>/<document_id>.html
corpus/pdf/agent_inputs/<category>/<document_id>.pdf
```

The `agent_inputs` segment keeps consistency-test artifacts separate from primary source files.

## Demo ingest

Case folders use **flat** filenames at `ingest/` root (e.g. `PMC6889286_article.xml`, `eln_experiment_notebook.txt`).

Built by `build_case_folders.py` from corpus paths defined in `scenarios.py`.
