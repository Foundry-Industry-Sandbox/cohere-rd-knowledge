# Agent Foundry Governance

This document describes how the HLS R&D knowledge mining demo bundles the [Agent Governance Toolkit (AGT)](https://microsoft.github.io/agent-governance-toolkit/) policy definitions with each Microsoft Foundry prompt agent.

## Policy bundle layout

Policies live beside each agent definition:

```text
agent-provisioning/src/CohereRndKnowledge.AgentProvisioning/agents/
  ingestion-translation-agent/
    agent.json
    instructions.md
    mcp.json
    governance.yaml
    rogue.yaml
  metadata-linking-agent/
    ...
  search-chat-agent/
    ...
  curation-compliance-agent/
    ...
```

## Provisioning traceability

Agent provisioning fingerprints include governance YAML content and are stored in each Foundry agent version's metadata. The provisioner CLI emits:

- `governanceToolkitVersion: 4.0.0`
- `policyBundleVersion: v1`
- `governedAgents: [...]`

Policy changes therefore bump the Foundry agent version fingerprint and trigger reprovisioning on the next deploy.

## Tool deny matrix

Each `governance.yaml` uses `default_action: allow` with explicit deny rules for cross-stage tools.

| Tool | ingestion | metadata | search | curation |
|------|:---------:|:--------:|:------:|:--------:|
| `search_rd_knowledge` | D | A | A | D |
| `get_knowledge_lineage` | D | D | A | D |
| `index_rd_knowledge` | D | A | D | D |
| `link_rd_metadata` | D | A | D | D |
| `curate_session_responses` | D | D | D | A |
| `read_fabric_raw_source` | A | D | D | D |
| `flag_sensitive_content` | D | D | A | A |
| `get_relevant_policies` | D | D | A | A |
| `get_policies_by_refs` | D | D | A | A |

**D** = explicit deny rule in `governance.yaml`; **A** = allowed via `default_action: allow`.

Per-agent deny rules live in `agent-provisioning/src/CohereRndKnowledge.AgentProvisioning/agents/*/governance.yaml`.

## Rogue risky tools

| Agent | `riskyTool` | Rationale |
|---|---|---|
| `ingestion-translation-agent` | `search_rd_knowledge` | Cross-block escalation to query/search tools |
| `metadata-linking-agent` | `search_rd_knowledge` | Same |
| `search-chat-agent` | `curate_session_responses` | Cross-block escalation to curation |
| `curation-compliance-agent` | `search_rd_knowledge` | Re-query bypass during compliance review |

Defaults: `windowSize: 10`, `triggerCount: 5`.

## Runtime enforcement

Governance is enforced on the **MCP server** before each tool handler runs:

1. **`McpAgentRoleMiddleware`** — validates `X-Agent-Role` against the MCP route (`/raw-source/mcp`, `/knowledge-search/mcp`, `/curation-compliance/mcp`).
2. **`GovernedMcpServerTool`** — wraps each MCP tool and delegates to `McpToolGovernanceCoordinator`.
3. **`McpToolGovernanceCoordinator`** — evaluates `governance.yaml`, applies rogue detection, writes audit records, and returns MCP errors for blocked calls.

At build time, `RndKnowledgeMining.Governance` copies co-located policy files into `policies/{agent-name}/` in the MCP server output directory.

Configuration in `RndKnowledgeMining.Mcp/appsettings.json`:

```json
"Governance": {
  "EnableMcpToolGovernance": true,
  "AgentAuditStoreDirectory": "data/agent-governance-audit"
}
```

Set `EnableMcpToolGovernance: false` to disable runtime policy enforcement. Mount a persistent volume for `AgentAuditStoreDirectory` in production.

To verify: run a workflow that triggers MCP tools, then inspect `data/agent-governance-audit/agent-governance-audit.jsonl` on the MCP host.
