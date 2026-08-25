# Troubleshooting

## Deployment issues

| Symptom | Possible cause | Resolution |
|---|---|---|
| ARM deployment fails | Region quota or name conflict | Try another region or change `baseName`. |
| Provisioning job failed | Foundry or MCP not ready | Check Container Apps Job logs; verify `mcpUrl` and model deployments |
| Frontend cannot reach API | Wrong `ApiBaseUrl` | Use `apiUrl` output; ensure ingress is enabled |

## Model availability issues

| Symptom | Possible cause | Resolution |
|---|---|---|
| `no_capacity` during agent calls | Prompt plus reserved output exceeds available TPM, or deployment capacity is exhausted | Keep prompts and `max_tokens` within the deployed TPM budget; increase `modelDeploymentCapacity` only when regional quota is available |
| Metadata linking gets HTTP 429 after ingestion succeeds | Ingestion and metadata linking share one rolling TPM pool; retrying the Responses request restarts the full tool sequence | Route metadata linking to the dedicated `metadataModelDeploymentName` deployment so it uses the separate Command A quota pool |
| Model not found | SKU or region unsupported | Confirm Cohere models are available in target region; adjust `cohereModelName` if catalog changed |
| Embed/rerank errors | Wrong deployment names | Match `embedDeploymentName` and `rerankDeploymentName` across infra and app settings |

## Agent provisioning issues

| Symptom | Possible cause | Resolution |
|---|---|---|
| Agent missing in Foundry | Provisioning job did not complete | Re-run provisioning CLI or redeploy |
| MCP tool errors in trace | `MCP_BASE_URL` incorrect | Set to deployed MCP FQDN without trailing path errors |
| Metadata agent still uses Command A+ | Dedicated deployment setting was omitted or agents were not reprovisioned | Set `AZURE_AI_METADATA_MODEL_DEPLOYMENT_NAME`, rerun provisioning, and verify the latest metadata agent version references that deployment |
| Governance blocks tool | `governance.yaml` denies tool or rogue detection triggered | Expected cross-stage boundary; verify correct agent and MCP route; inspect `data/agent-governance-audit` on MCP host |

## Backend startup issues

| Symptom | Possible cause | Resolution |
|---|---|---|
| Api.Host fails on start | Missing Azure configuration | Provide Search, Foundry, and Storage settings |
| MCP requires Azure | No local fallback | Configure `appsettings.Deployment.local.json` with real endpoints |
| Dataset not found | Wrong `Dataset:RootPath` | Run from Api.Host project; path defaults to `../../../dataset-seed` |

## Frontend connection issues

| Symptom | Possible cause | Resolution |
|---|---|---|
| 404 or connection refused | `ApiBaseUrl` mismatch | Set `http://localhost:8080/` for local dev |
| Scenario missing | `PortfolioScenarios` not updated | Sync `appsettings.json` with `dataset-seed/cases/catalog.json` |
| Polling timeout | Long workflow | Increase `WorkflowPolling:MaxDurationMinutes` |

## Dataset issues

| Symptom | Possible cause | Resolution |
|---|---|---|
| QRY-002 returns no citations | KB empty | Run ING-001 first |
| QRY-001 shows citations | KB not cleared | Use **Reset demo** on the portfolio page, or call `POST /api/rd-knowledge/vector-db/clear-data` |
| Ingest files not found | Wrong `sourceId` | Match `caseFolder` in start payload |

## Quota and regional availability

| Symptom | Possible cause | Resolution |
|---|---|---|
| Search SKU errors | Tier limits | Review AI Search SKU in `data-services.bicep` |
| Container Apps throttling | Low replicas | Scale API/MCP apps in Azure portal |
| Fabric provisioning fails | Prerequisites not met | In the **same resource group** as the deploy, run `.\infra\scripts\setup-fabric-provision-identity.ps1 -ResourceGroupName <rg> -WorkspaceName <ws>` (identity defaults to `fabric-identity`); or deploy with `enableFabric: false` |

Additional references: [`configuration.md`](./configuration.md), [`../infra/README.md`](../infra/README.md).
