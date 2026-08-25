param location string
param resourceTags object
param foundryAccountName string
param foundryProjectName string
param modelDeploymentName string
param modelDeploymentSkuName string
param modelDeploymentCapacity int
param cohereModelName string
param cohereModelVersion string
param metadataModelDeploymentName string
param metadataModelDeploymentSkuName string
param metadataModelDeploymentCapacity int
param metadataCohereModelName string
param metadataCohereModelVersion string
param embedDeploymentName string
param embedModelName string
param embedModelVersion string
param embedDeploymentSkuName string
param embedDeploymentCapacity int
param rerankDeploymentName string
param rerankModelName string
param rerankModelVersion string
param rerankDeploymentSkuName string
param rerankDeploymentCapacity int
param applicationInsightsId string
@secure()
param applicationInsightsConnectionString string

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: foundryAccountName
  location: location
  tags: resourceTags
  kind: 'AIServices'
  sku: {
    name: 'S0'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    allowProjectManagement: true
    customSubDomainName: foundryAccountName
    publicNetworkAccess: 'Enabled'
  }
}

resource foundryProject 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = {
  parent: foundryAccount
  name: foundryProjectName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {}
}

resource appInsightsConnection 'Microsoft.CognitiveServices/accounts/projects/connections@2025-06-01' = {
  parent: foundryProject
  name: 'appinsights'
  properties: {
    authType: 'ApiKey'
    category: 'AppInsights'
    target: applicationInsightsId
    useWorkspaceManagedIdentity: false
    isSharedToAll: true
    peRequirement: 'NotRequired'
    peStatus: 'NotApplicable'
    metadata: {
      ApiType: 'Azure'
      ResourceId: applicationInsightsId
    }
    credentials: {
      key: applicationInsightsConnectionString
    }
  }
}

resource modelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundryAccount
  name: modelDeploymentName
  sku: {
    name: modelDeploymentSkuName
    capacity: modelDeploymentCapacity
  }
  properties: {
    model: {
      format: 'Cohere'
      name: cohereModelName
      version: cohereModelVersion
    }
  }
  dependsOn: [
    foundryProject
  ]
}

resource metadataModelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundryAccount
  name: metadataModelDeploymentName
  sku: {
    name: metadataModelDeploymentSkuName
    capacity: metadataModelDeploymentCapacity
  }
  properties: {
    model: {
      format: 'Cohere'
      name: metadataCohereModelName
      version: metadataCohereModelVersion
    }
  }
  dependsOn: [
    foundryProject
    modelDeployment
  ]
}

resource embedModelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundryAccount
  name: embedDeploymentName
  sku: {
    name: embedDeploymentSkuName
    capacity: embedDeploymentCapacity
  }
  properties: {
    model: {
      format: 'Cohere'
      name: embedModelName
      version: embedModelVersion
    }
  }
  dependsOn: [
    foundryProject
    metadataModelDeployment
  ]
}

resource rerankModelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundryAccount
  name: rerankDeploymentName
  sku: {
    name: rerankDeploymentSkuName
    capacity: rerankDeploymentCapacity
  }
  properties: {
    model: {
      format: 'Cohere'
      name: rerankModelName
      version: rerankModelVersion
    }
  }
  dependsOn: [
    foundryProject
    embedModelDeployment
  ]
}

var foundryEndpointBase = 'https://${foundryAccount.properties.customSubDomainName}.services.ai.azure.com'
var embedEndpoint = '${foundryEndpointBase}/openai/deployments/${embedDeploymentName}'
var rerankEndpoint = foundryEndpointBase
var foundryProjectEndpoint = '${foundryEndpointBase}/api/projects/${foundryProject.name}'

output foundryAccountName string = foundryAccount.name
output foundryAccountId string = foundryAccount.id
output foundryProjectName string = foundryProject.name
output foundryProjectResourceId string = foundryProject.id
output foundryProjectPrincipalId string = foundryProject.identity.principalId
output foundryProjectEndpoint string = foundryProjectEndpoint
output embedEndpoint string = embedEndpoint
output rerankEndpoint string = rerankEndpoint
output modelDeploymentName string = modelDeploymentName
output metadataModelDeploymentName string = metadataModelDeploymentName
output embedDeploymentName string = embedDeploymentName
output embedModelName string = embedModelName
output rerankDeploymentName string = rerankDeploymentName
output rerankModelName string = rerankModelName
