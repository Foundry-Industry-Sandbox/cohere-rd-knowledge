# Deployment parameters

Parameters for the custom Deploy to Azure template ([`infra/mainTemplate.json`](../infra/mainTemplate.json)), defined in [`infra/main.bicep`](../infra/main.bicep). The portal UI is driven by [`infra/createUiDefinition.json`](../infra/createUiDefinition.json).

## Template parameters

| Parameter | Default | Description |
|---|---|---|
| `location` | Resource group location | Azure region for deployed resources. The Deploy to Azure experience supplies the region selected in Basics. |
| `baseName` | `coherernd` | Short prefix used for deployed resource names. Region is selected in the Deploy to Azure basics step. |
| `foundryProjectName` | `project` | Foundry project suffix. Deployed as `{baseName}-project` with the default value. |
| `modelDeploymentName` | `cohere-command-a-plus` | Foundry model deployment name used by all prompt agents except metadata linking. |
| `modelDeploymentSkuName` | `GlobalStandard` | SKU for the agent model deployment. Use `GlobalStandard` for serverless; use a provisioned SKU only if available for the model and region. |
| `modelDeploymentCapacity` | `20` | Capacity units for the agent model deployment. Increase only after confirming regional quota is available. |
| `cohereModelName` | `Cohere-command-a-plus-05-2026` | Cohere Command A model name in the Foundry catalog. |
| `cohereModelVersion` | `1` | Cohere Command A model version. |
| `metadataModelDeploymentName` | `cohere-command-a` | Dedicated Foundry deployment name used by the metadata-linking agent. |
| `metadataModelDeploymentSkuName` | `GlobalStandard` | SKU for the dedicated metadata-linking deployment. |
| `metadataModelDeploymentCapacity` | `100` | Capacity units for metadata linking. The default targets the separate 100K TPM Command A quota pool. |
| `metadataCohereModelName` | `cohere-command-a` | Cohere Command A model name used by metadata linking. |
| `metadataCohereModelVersion` | `1` | Cohere Command A model version used by metadata linking. |
| `embedDeploymentName` | `cohere-embed-v4` | Foundry deployment name for Cohere embed-v4. |
| `embedModelName` | `embed-v-4-0` | Cohere embed model name in the Foundry catalog. |
| `embedModelVersion` | `1` | Cohere embed model version. |
| `embedDeploymentSkuName` | `GlobalStandard` | SKU for the embed deployment. Select only a SKU available for the model and region. |
| `embedDeploymentCapacity` | `10` | Capacity units for the embed deployment. Increase for faster knowledge indexing and fewer throttling failures. |
| `rerankDeploymentName` | `cohere-rerank-v4-pro` | Foundry deployment name for Cohere rerank v4 pro. |
| `rerankModelName` | `Cohere-rerank-v4.0-pro` | Cohere rerank model name in the Foundry catalog. |
| `rerankModelVersion` | `1` | Cohere rerank model version. |
| `rerankDeploymentSkuName` | `GlobalStandard` | SKU for the rerank deployment. Select only a SKU available for the model and region. |
| `rerankDeploymentCapacity` | `5` | Capacity units for the rerank deployment. Increase for faster retrieval reranking and fewer throttling failures. |
| `documentsContainerName` | `rd-knowledge-documents` | Blob container for ingested R&D knowledge documents. |
| `searchSku` | `basic` | Azure AI Search SKU for retrieval indexes. |
| `apiContainerImage` | `ghcr.io/foundry-industry-sandbox/coherernd-api:demo` | Full container image URI for the API host Container App. |
| `mcpContainerImage` | `ghcr.io/foundry-industry-sandbox/coherernd-mcp:demo` | Full container image URI for the MCP host Container App. |
| `frontendContainerImage` | `ghcr.io/foundry-industry-sandbox/coherernd-web:demo` | Full container image URI for the frontend Container App. |
| `provisioningContainerImage` | `ghcr.io/foundry-industry-sandbox/coherernd-provisioning:demo` | Full container image URI for the agent provisioning Container Apps Job. |
| `enableFabric` | `false` | When `true`, provisions a Fabric lakehouse, seeds `dataset-seed/cases/*/ingest` into OneLake at `Files/bronze/cases/{caseId}/ingest/...`, configures API + MCP for Fabric raw sources, and uses `fabricIdentityName` as the shared UAMI for both Container Apps (no separate api/mcp identities). Requires `fabricWorkspaceName`. Fabric support in this demo is **not production-ready**. |
| `fabricWorkspaceName` | `''` | Fabric workspace name. Required when `enableFabric` is `true`. Must be capacity-backed and accessible to the operator. |
| `fabricLakehouseName` | `RDKnowledgeLakehouse` | Fabric lakehouse name. Created at deploy time if missing when Fabric is enabled. |
| `fabricIdentityName` | `fabric-identity` | Optional. Name of an existing user-assigned managed identity in the **same resource group** as the deployment. Defaults to `fabric-identity`. Create with `infra/scripts/setup-fabric-provision-identity.ps1`. Shared by API, MCP, and Fabric seed scripts when Fabric is enabled. |
| `fabricRepositoryArchiveUrl` | `https://github.com/foundry-industry-sandbox/cohere-rd-knowledge/archive/refs/heads/main.zip` | Repository archive URL the Fabric seed script downloads to fetch `infra/scripts/` and `dataset-seed/`. |

Prebuilt container images are published to Docker Hub by [`.github/workflows/publish-container-images.yml`](../.github/workflows/publish-container-images.yml) using the `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN` repository secrets. Keep the `foundryindustrysandbox/coherernd-*` repositories public so Azure Container Apps can pull them without registry credentials.

## Deployment outputs

After a successful deployment, use these outputs:

| Output | Use |
|---|---|
| `frontendUrl` | Open the demo UI |
| `apiUrl` | Backend REST API |
| `mcpUrl` | MCP base URL (used during agent provisioning) |
| `foundryProjectEndpoint` | Microsoft Foundry project endpoint |
| `searchServiceEndpoint` | Azure AI Search endpoint |
| `modelDeploymentName` | Command A+ deployment name |
| `metadataModelDeploymentName` | Dedicated metadata-linking Command A deployment name |
| `embedDeploymentName` / `rerankDeploymentName` | RAG model deployments |

See also [`infra/main.bicep`](../infra/main.bicep).
