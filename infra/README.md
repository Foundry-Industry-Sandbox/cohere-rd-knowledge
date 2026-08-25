# Infrastructure

Bicep templates and scripts to deploy the sample into an Azure subscription. Data generation is not deployed.

## Resources deployed

| Resource | Purpose |
|---|---|
| Microsoft Foundry account and project | Prompt agents and model deployments |
| Cohere model deployments | Command A+ (primary agents), Command A (metadata linking), embed-v4, rerank-v4-pro |
| Azure AI Search | Knowledge and policy retrieval indexes |
| Storage account | Normalized documents |
| Container Apps Environment | Hosts API, MCP, and frontend |
| Container Apps Jobs | Policy seed and agent provisioning |
| Azure Container Instances | Runs the post-deploy jobs with managed identity |
| User-assigned managed identities | API, MCP, and provisioning workloads |
| Log Analytics / Application Insights | Telemetry |
| Microsoft Fabric (optional) | Lakehouse for raw data when enabled |

The default Container Apps environment is not VNet-integrated, so the storage
account keeps its HTTPS public endpoint enabled. Blob access is still
Microsoft Entra-only: shared-key authentication and anonymous blob access are
disabled, and workload identities require data-plane RBAC. The storage-only
`SecurityControl=Ignore` tag prevents the organization policy that otherwise
rewrites `publicNetworkAccess` to `Disabled` from breaking this network path.
Deployments that require private-only storage must instead VNet-integrate the
Container Apps environment and add a Blob private endpoint and private DNS.

## Bicep modules

| Module | File |
|---|---|
| Naming | `modules/naming.bicep` |
| Platform | `modules/platform.bicep` |
| Data services | `modules/data-services.bicep` |
| Foundry | `modules/foundry.bicep` |
| Security / RBAC | `modules/security.bicep` |
| Container Apps | `modules/container-apps.bicep` |
| Container Jobs | `modules/container-jobs.bicep` |
| Fabric (optional) | `modules/fabric-provision.bicep` |
| Post-deploy runner | `modules/post-deploy-scripts.bicep` |

Entry point: [`main.bicep`](main.bicep).

## Parameters

Key parameters in `main.bicep`:

| Parameter | Default | Description |
|---|---|---|
| `location` | Resource group location | Azure region for deployed resources |
| `baseName` | `coherernd` | Resource name prefix |
| `modelDeploymentName` | `cohere-command-a-plus` | Primary agent model deployment |
| `modelDeploymentCapacity` | `20` | Primary agent-model capacity; raise only when regional quota is available |
| `metadataModelDeploymentName` | `cohere-command-a` | Dedicated metadata-linking model deployment |
| `metadataModelDeploymentCapacity` | `100` | Metadata capacity on the separate Command A quota pool |
| `enableFabric` | `false` | Enable Fabric lakehouse integration (demo; not production-ready) |
| `fabricIdentityName` | `fabric-identity` | Existing UAMI name in the same RG (used when Fabric is enabled) |
| `apiContainerImage` | `ghcr.io/foundry-industry-sandbox/coherernd-api:demo` | API container image |
| `mcpContainerImage` | `ghcr.io/foundry-industry-sandbox/coherernd-mcp:demo` | MCP container image |
| `frontendContainerImage` | `ghcr.io/foundry-industry-sandbox/coherernd-web:demo` | Frontend container image |

See the Deploy to Azure UI in [`createUiDefinition.json`](createUiDefinition.json). Full parameter reference: [`docs/deployment-parameters.md`](../docs/deployment-parameters.md).

## Post-deploy runner

`modules/post-deploy-scripts.bicep` deploys one Azure Container Instances runner with the post-deploy user-assigned managed identity. The runner explicitly signs in with `az login --identity`, starts the policy seed job, waits for it to succeed, and then starts agent provisioning. This avoids the shared-key storage staging required by ARM deployment scripts.

ARM reports the container group as provisioned before its command finishes. Confirm the runner reaches `Succeeded` after the template deployment:

```bash
runnerName=$(az container list --resource-group <resource-group> --query "[?starts_with(name, 'run-post-deploy-')].name | [0]" --output tsv)
az container show --resource-group <resource-group> --name "$runnerName" --query instanceView.state --output tsv
az container logs --resource-group <resource-group> --name "$runnerName"
```

## Outputs

| Output | Use |
|---|---|
| `frontendUrl` | Open the demo UI |
| `apiUrl` | Backend REST API |
| `mcpUrl` | MCP base URL for agent tools |
| `foundryProjectEndpoint` | Microsoft Foundry project endpoint |
| `searchServiceEndpoint` | Azure AI Search endpoint |

## Validate ARM template source

The repository includes a classic, array-based Partner Center ARM template at [`mainTemplate.json`](mainTemplate.json). Validate Bicep changes without overwriting that artifact:

```bash
az bicep lint --file infra/main.bicep
az bicep build --file infra/main.bicep --outfile <temporary-output-path>
```

Current Bicep emits `languageVersion: 2.0` with symbolic resources, while the Partner Center artifact must remain in classic ARM format and retain the `pid-f9e6e8a6-13d8-4142-a9ed-132c3108d4da-partnercenter` attribution deployment. Apply equivalent source changes to the classic template, run ARM-TTK, and refresh `partner-center/AzureApplicationPackage-Cohere-HLS.zip`.

## Deploy to Azure button support

The root README **Deploy to Azure** button targets [`mainTemplate.json`](mainTemplate.json) with [`createUiDefinition.json`](createUiDefinition.json) via the hosted raw GitHub URIs in the root `README.md`.

After changing Bicep, update the classic `mainTemplate.json` and commit it before the button reflects your changes.

## Cleanup

```bash
az group delete --name <resource-group-name> --yes --no-wait
```

Additional scripts:

| Script | Purpose |
|---|---|
| `scripts/setup-fabric-provision-identity.ps1` | Pre-deploy: create UAMI in the deploy RG and assign Fabric workspace Contributor (`-ResourceGroupName`, `-WorkspaceName`; optional `-IdentityName`, default `fabric-identity`). See [docs/fabric-setup.md](../docs/fabric-setup.md). |
| `scripts/provision-fabric-lakehouse.ps1` | Fabric lakehouse provisioning helper |
| `scripts/seed-fabric-data.ps1` | Deploy-time Fabric seed orchestrator (downloads dataset-seed, uploads bronze) |
| `scripts/seed-fabric-bronze.ps1` | Upload `cases/*/ingest` → `Files/bronze/cases/{caseId}/ingest/` |
