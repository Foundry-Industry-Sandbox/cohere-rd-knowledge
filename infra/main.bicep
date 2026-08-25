@description('Azure region for deployed resources.')
param location string = resourceGroup().location

@description('Base name used for deployed resources.')
param baseName string = 'coherernd'

@description('Foundry project suffix. The deployed project is named {baseName}-{foundryProjectName}.')
param foundryProjectName string = 'project'

@description('Foundry model deployment name used by all agents except metadata linking.')
param modelDeploymentName string = 'cohere-command-a-plus'

@description('SKU used by the Foundry model deployment for the agents. Use GlobalStandard for serverless deployments; use a provisioned SKU only if it is available for the model and region.')
param modelDeploymentSkuName string = 'GlobalStandard'

@minValue(1)
@maxValue(1000000)
@description('Capacity units for the Foundry model deployment used by the agents. The default matches the baseline quota; increase it only after confirming regional quota is available.')
param modelDeploymentCapacity int = 20

@description('Cohere Command A model name in Foundry catalog.')
param cohereModelName string = 'Cohere-command-a-plus-05-2026'

@description('Cohere Command A model version.')
param cohereModelVersion string = '1'

@description('Dedicated Foundry deployment name used by the metadata-linking agent.')
param metadataModelDeploymentName string = 'cohere-command-a'

@description('SKU used by the dedicated metadata-linking model deployment.')
param metadataModelDeploymentSkuName string = 'GlobalStandard'

@minValue(1)
@maxValue(1000000)
@description('Capacity units for the dedicated metadata-linking deployment. The default uses the separate 100K TPM Command A quota pool.')
param metadataModelDeploymentCapacity int = 100

@description('Cohere Command A model name used by the metadata-linking agent.')
param metadataCohereModelName string = 'cohere-command-a'

@description('Cohere Command A model version used by the metadata-linking agent.')
param metadataCohereModelVersion string = '1'

@description('Foundry deployment name for Cohere embed-v-4-0.')
param embedDeploymentName string = 'cohere-embed-v4'

@description('Cohere embed model name in Foundry catalog.')
param embedModelName string = 'embed-v-4-0'

@description('Cohere embed model version.')
param embedModelVersion string = '1'

@description('SKU used by the Cohere embed deployment. Use a SKU only if it is available for the model and region.')
param embedDeploymentSkuName string = 'GlobalStandard'

@minValue(1)
@maxValue(1000000)
@description('Capacity units for the Cohere embed deployment. Increase this for faster knowledge indexing and fewer throttling failures.')
param embedDeploymentCapacity int = 10

@description('Foundry deployment name for Cohere-rerank-v4.0-pro.')
param rerankDeploymentName string = 'cohere-rerank-v4-pro'

@description('Cohere rerank model name in Foundry catalog.')
param rerankModelName string = 'Cohere-rerank-v4.0-pro'

@description('Cohere rerank model version.')
param rerankModelVersion string = '1'

@description('SKU used by the Cohere rerank deployment. Use a SKU only if it is available for the model and region.')
param rerankDeploymentSkuName string = 'GlobalStandard'

@minValue(1)
@maxValue(1000000)
@description('Capacity units for the Cohere rerank deployment. Increase this for faster retrieval reranking and fewer throttling failures.')
param rerankDeploymentCapacity int = 5

@description('Blob container for ingested R&D knowledge documents.')
param documentsContainerName string = 'rd-knowledge-documents'

@description('Azure AI Search SKU for retrieval indexes.')
param searchSku string = 'basic'

@description('Full container image URI for the API host.')
param apiContainerImage string = 'ghcr.io/foundry-industry-sandbox/coherernd-api:demo'

@description('Full container image URI for the MCP host.')
param mcpContainerImage string = 'ghcr.io/foundry-industry-sandbox/coherernd-mcp:demo'

@description('Full container image URI for the frontend host.')
param frontendContainerImage string = 'ghcr.io/foundry-industry-sandbox/coherernd-web:demo'

@description('Full container image URI for the agent provisioning job.')
param provisioningContainerImage string = 'ghcr.io/foundry-industry-sandbox/coherernd-provisioning:demo'

@description('When true, provisions Fabric lakehouse, seeds lakehouse data, and configures API + MCP to use the existing fabric identity (fabricIdentityName) for OneLake and Azure RBAC. Requires fabricWorkspaceName.')
param enableFabric bool = false

@description('Fabric workspace name. Required when enableFabric is true. Must be capacity-backed and accessible to the operator.')
param fabricWorkspaceName string = ''

@description('Fabric lakehouse name. Used only when enableFabric is true. Created at deploy time if missing.')
param fabricLakehouseName string = 'RDKnowledgeLakehouse'

@description('Name of an existing user-assigned managed identity in this resource group. Optional; defaults to fabric-identity. Create with setup-fabric-provision-identity.ps1 (same RG as this deployment) with a Fabric workspace role. Used as the MCP container app identity and as the Fabric seed deployment script identity. Its client ID is auto-derived by the deployment.')
param fabricIdentityName string = 'fabric-identity'

@description('Repository archive URL for the seed script to download infra/scripts/ and dataset-seed/.')
param fabricRepositoryArchiveUrl string = 'https://github.com/foundry-industry-sandbox/cohere-rd-knowledge/archive/refs/heads/main.zip'

var resourceTags = {
  project: 'cohere-rd-knowledge'
}

resource resourceGroupTags 'Microsoft.Resources/tags@2023-07-01' = {
  name: 'default'
  properties: {
    tags: resourceTags
  }
}

module naming 'modules/naming.bicep' = {
  name: 'naming'
  params: {
    baseName: baseName
  }
}

module dataServices 'modules/data-services.bicep' = {
  name: 'data-services'
  params: {
    location: location
    resourceTags: resourceTags
    storageAccountName: naming.outputs.storageAccountName
    documentsContainerName: documentsContainerName
    searchServiceName: naming.outputs.searchServiceName
    searchSku: searchSku
  }
}

module foundry 'modules/foundry.bicep' = {
  name: 'foundry'
  params: {
    location: location
    resourceTags: resourceTags
    foundryAccountName: naming.outputs.foundryAccountName
    foundryProjectName: '${baseName}-${foundryProjectName}'
    modelDeploymentName: modelDeploymentName
    modelDeploymentSkuName: modelDeploymentSkuName
    modelDeploymentCapacity: modelDeploymentCapacity
    cohereModelName: cohereModelName
    cohereModelVersion: cohereModelVersion
    metadataModelDeploymentName: metadataModelDeploymentName
    metadataModelDeploymentSkuName: metadataModelDeploymentSkuName
    metadataModelDeploymentCapacity: metadataModelDeploymentCapacity
    metadataCohereModelName: metadataCohereModelName
    metadataCohereModelVersion: metadataCohereModelVersion
    embedDeploymentName: embedDeploymentName
    embedModelName: embedModelName
    embedModelVersion: embedModelVersion
    embedDeploymentSkuName: embedDeploymentSkuName
    embedDeploymentCapacity: embedDeploymentCapacity
    rerankDeploymentName: rerankDeploymentName
    rerankModelName: rerankModelName
    rerankModelVersion: rerankModelVersion
    rerankDeploymentSkuName: rerankDeploymentSkuName
    rerankDeploymentCapacity: rerankDeploymentCapacity
    applicationInsightsId: platform.outputs.applicationInsightsId
    applicationInsightsConnectionString: platform.outputs.applicationInsightsConnectionString
  }
}

module platform 'modules/platform.bicep' = {
  name: 'platform'
  params: {
    location: location
    resourceTags: resourceTags
    logAnalyticsName: naming.outputs.logAnalyticsName
    applicationInsightsName: naming.outputs.applicationInsightsName
    containerAppsEnvironmentName: naming.outputs.containerAppsEnvironmentName
  }
}

module security 'modules/security.bicep' = {
  name: 'security'
  params: {
    location: location
    resourceTags: resourceTags
    deploymentSuffix: naming.outputs.deploymentSuffix
    apiIdentityName: naming.outputs.apiIdentityName
    mcpIdentityName: naming.outputs.mcpIdentityName
    provisioningIdentityName: naming.outputs.provisioningIdentityName
    storageAccountName: dataServices.outputs.storageAccountName
    foundryAccountName: foundry.outputs.foundryAccountName
    foundryProjectName: foundry.outputs.foundryProjectName
    searchServiceName: dataServices.outputs.searchServiceName
    enableFabric: enableFabric
    fabricIdentityName: fabricIdentityName
  }
}

module containerApps 'modules/container-apps.bicep' = {
  name: 'container-apps'
  params: {
    location: location
    resourceTags: resourceTags
    containerAppsEnvironmentId: platform.outputs.containerAppsEnvironmentId
    apiAppName: naming.outputs.apiAppName
    mcpAppName: naming.outputs.mcpAppName
    frontendAppName: naming.outputs.frontendAppName
    apiContainerImage: apiContainerImage
    mcpContainerImage: mcpContainerImage
    frontendContainerImage: frontendContainerImage
    apiIdentityId: security.outputs.apiIdentityId
    apiIdentityClientId: security.outputs.apiIdentityClientId
    mcpIdentityId: security.outputs.mcpIdentityId
    mcpIdentityClientId: security.outputs.mcpIdentityClientId
    foundryProjectEndpoint: foundry.outputs.foundryProjectEndpoint
    blobServiceUri: dataServices.outputs.blobServiceUri
    documentsContainerName: dataServices.outputs.documentsContainerName
    searchServiceEndpoint: dataServices.outputs.searchServiceEndpoint
    embedDeploymentName: foundry.outputs.embedDeploymentName
    embedModelName: foundry.outputs.embedModelName
    rerankDeploymentName: foundry.outputs.rerankDeploymentName
    rerankModelName: foundry.outputs.rerankModelName
    embedEndpoint: foundry.outputs.embedEndpoint
    rerankEndpoint: foundry.outputs.rerankEndpoint
    enableFabric: enableFabric
    fabricWorkspaceName: fabricWorkspaceName
    fabricLakehouseName: fabricLakehouseName
    applicationInsightsConnectionString: platform.outputs.applicationInsightsConnectionString
  }
}

module containerJobs 'modules/container-jobs.bicep' = {
  name: 'container-jobs'
  params: {
    location: location
    resourceTags: resourceTags
    containerAppsEnvironmentId: platform.outputs.containerAppsEnvironmentId
    policySeedJobName: naming.outputs.policySeedJobName
    provisioningJobName: naming.outputs.provisioningJobName
    mcpContainerImage: mcpContainerImage
    provisioningContainerImage: provisioningContainerImage
    mcpIdentityId: security.outputs.mcpIdentityId
    provisioningIdentityId: security.outputs.provisioningIdentityId
    provisioningIdentityClientId: security.outputs.provisioningIdentityClientId
    mcpUrl: containerApps.outputs.mcpUrl
    mcpContainerEnv: containerApps.outputs.mcpContainerEnv
    foundryProjectEndpoint: foundry.outputs.foundryProjectEndpoint
    modelDeploymentName: foundry.outputs.modelDeploymentName
    metadataModelDeploymentName: foundry.outputs.metadataModelDeploymentName
    applicationInsightsConnectionString: platform.outputs.applicationInsightsConnectionString
  }
}

module postDeployScripts 'modules/post-deploy-scripts.bicep' = {
  name: 'post-deploy-scripts'
  params: {
    location: location
    resourceTags: resourceTags
    deploymentSuffix: naming.outputs.deploymentSuffix
    deploymentScriptIdentityName: naming.outputs.deploymentScriptIdentityName
    policySeedIdentityId: security.outputs.mcpIdentityId
    policySeedIdentityClientId: security.outputs.mcpIdentityClientId
    policySeedEmbedEndpoint: foundry.outputs.embedEndpoint
    policySeedEmbedModelName: foundry.outputs.embedModelName
    policySeedEmbeddingDimensions: 1024
    policySeedJobName: containerJobs.outputs.policySeedJobName
    provisioningJobName: containerJobs.outputs.provisioningJobName
  }
}

module fabricProvision 'modules/fabric-provision.bicep' = if (enableFabric) {
  name: 'fabric-provision'
  params: {
    location: location
    resourceTags: resourceTags
    deploymentSuffix: naming.outputs.deploymentSuffix
    fabricIdentityName: fabricIdentityName
    fabricWorkspaceName: fabricWorkspaceName
    fabricLakehouseName: fabricLakehouseName
  }
}

module fabricSeed 'modules/fabric-seed.bicep' = if (enableFabric) {
  name: 'fabric-seed'
  params: {
    location: location
    resourceTags: resourceTags
    deploymentSuffix: naming.outputs.deploymentSuffix
    fabricIdentityName: fabricIdentityName
    fabricWorkspaceId: fabricProvision!.outputs.workspaceId
    fabricWorkspaceName: fabricProvision!.outputs.workspaceName
    fabricLakehouseId: fabricProvision!.outputs.lakehouseId
    fabricLakehouseName: fabricProvision!.outputs.lakehouseName
    fabricRepositoryArchiveUrl: fabricRepositoryArchiveUrl
  }
}

output storageAccountName string = dataServices.outputs.storageAccountName
output blobServiceUri string = dataServices.outputs.blobServiceUri
output documentsContainerName string = dataServices.outputs.documentsContainerName
output foundryAccountName string = foundry.outputs.foundryAccountName
output foundryProjectName string = foundry.outputs.foundryProjectName
output foundryProjectEndpoint string = foundry.outputs.foundryProjectEndpoint
output foundryProjectResourceId string = foundry.outputs.foundryProjectResourceId
output modelDeploymentName string = foundry.outputs.modelDeploymentName
output metadataModelDeploymentName string = foundry.outputs.metadataModelDeploymentName
output embedDeploymentName string = foundry.outputs.embedDeploymentName
output embedModelName string = foundry.outputs.embedModelName
output rerankDeploymentName string = foundry.outputs.rerankDeploymentName
output rerankModelName string = foundry.outputs.rerankModelName
output searchServiceName string = dataServices.outputs.searchServiceName
output searchServiceEndpoint string = dataServices.outputs.searchServiceEndpoint
output apiUrl string = containerApps.outputs.apiUrl
output frontendUrl string = containerApps.outputs.frontendUrl
output mcpUrl string = containerApps.outputs.mcpUrl
output policySeedJobName string = containerJobs.outputs.policySeedJobName
output provisioningJobName string = containerJobs.outputs.provisioningJobName
output fabricWorkspaceId string = enableFabric ? fabricProvision!.outputs.workspaceId : ''
output fabricWorkspaceName string = enableFabric ? fabricProvision!.outputs.workspaceName : ''
output fabricLakehouseId string = enableFabric ? fabricProvision!.outputs.lakehouseId : ''
output fabricLakehouseName string = enableFabric ? fabricProvision!.outputs.lakehouseName : ''
