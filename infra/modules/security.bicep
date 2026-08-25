param location string
param resourceTags object
param deploymentSuffix string
param apiIdentityName string
param mcpIdentityName string
param provisioningIdentityName string
param storageAccountName string
param foundryAccountName string
param foundryProjectName string
param searchServiceName string
param enableFabric bool = false
param fabricIdentityName string = 'fabric-identity'

resource storageAccount 'Microsoft.Storage/storageAccounts@2025-01-01' existing = {
  name: storageAccountName
}

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: foundryAccountName
}

resource foundryProject 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' existing = {
  parent: foundryAccount
  name: foundryProjectName
}

resource searchService 'Microsoft.Search/searchServices@2025-05-01' existing = {
  name: searchServiceName
}

resource fabricUami 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = if (enableFabric) {
  name: fabricIdentityName
}

// When Fabric is enabled, API and MCP share fabricIdentity (Contributor on the Fabric workspace).
// When Fabric is disabled, each app gets its own UAMI.
resource mcpIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = if (!enableFabric) {
  name: mcpIdentityName
  location: location
  tags: resourceTags
}

resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = if (!enableFabric) {
  name: apiIdentityName
  location: location
  tags: resourceTags
}

var sharedFabricIdentityId = enableFabric ? fabricUami!.id : ''
var sharedFabricClientId = enableFabric ? fabricUami!.properties.clientId : ''
var sharedFabricPrincipalId = enableFabric ? fabricUami!.properties.principalId : ''

var apiIdentityResourceId = enableFabric ? sharedFabricIdentityId : apiIdentity!.id
var apiIdentityClientIdValue = enableFabric ? sharedFabricClientId : apiIdentity!.properties.clientId
var apiPrincipalId = enableFabric ? sharedFabricPrincipalId : apiIdentity!.properties.principalId

var mcpIdentityResourceId = enableFabric ? sharedFabricIdentityId : mcpIdentity!.id
var mcpIdentityClientIdValue = enableFabric ? sharedFabricClientId : mcpIdentity!.properties.clientId
var mcpPrincipalId = enableFabric ? sharedFabricPrincipalId : mcpIdentity!.properties.principalId

resource provisioningIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: provisioningIdentityName
  location: location
  tags: resourceTags
}

// API Azure RBAC — skipped when enableFabric (same principal already covered by mcp* assignments below).
resource apiStorageRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!enableFabric) {
  name: guid(storageAccount.id, apiIdentityResourceId, 'StorageBlobDataContributor', deploymentSuffix)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource apiFoundryRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!enableFabric) {
  name: guid(foundryAccount.id, apiIdentityResourceId, 'CognitiveServicesUser', deploymentSuffix)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'a97b65f3-24c7-4388-baec-2e87135dc908')
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [
    foundryProject
  ]
}

resource projectFoundryUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccount.id, foundryProject.id, 'FoundryUser', deploymentSuffix)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '53ca6127-db72-4b80-b1b0-d745d6d5456d')
    principalId: foundryProject.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource apiSearchContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!enableFabric) {
  name: guid(searchService.id, apiIdentityResourceId, 'SearchServiceContributor', deploymentSuffix)
  scope: searchService
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7ca78c08-252a-4471-8644-bb5ff32d4ba0')
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource apiSearchDataRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!enableFabric) {
  name: guid(searchService.id, apiIdentityResourceId, 'SearchIndexDataContributor', deploymentSuffix)
  scope: searchService
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '8ebe5a00-799e-43f5-93ac-243d3dce84a7')
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource mcpStorageRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, mcpIdentityResourceId, 'StorageBlobDataContributor', deploymentSuffix)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalId: mcpPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Keep this nested so the runtime-resolved Fabric identity principal can be used in
// the deterministic role-assignment name without creating a duplicate on upgrades.
module mcpFoundryInferenceRbac 'foundry-inference-rbac.bicep' = {
  name: 'mcp-foundry-inference-rbac'
  params: {
    foundryAccountName: foundryAccount.name
    principalId: mcpPrincipalId
  }
}

resource mcpSearchContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(searchService.id, mcpIdentityResourceId, 'SearchServiceContributor', deploymentSuffix)
  scope: searchService
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7ca78c08-252a-4471-8644-bb5ff32d4ba0')
    principalId: mcpPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource mcpSearchDataRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(searchService.id, mcpIdentityResourceId, 'SearchIndexDataContributor', deploymentSuffix)
  scope: searchService
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '8ebe5a00-799e-43f5-93ac-243d3dce84a7')
    principalId: mcpPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource provisioningFoundryRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccount.id, provisioningIdentity.id, 'CognitiveServicesContributor', deploymentSuffix)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '25fbc0a9-bd7c-42a3-aa1a-3b75d497ee68')
    principalId: provisioningIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [
    foundryProject
  ]
}

resource provisioningFoundryDeveloperRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccount.id, provisioningIdentity.id, 'FoundryUser', deploymentSuffix)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '53ca6127-db72-4b80-b1b0-d745d6d5456d')
    principalId: provisioningIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
  dependsOn: [
    foundryProject
  ]
}

output apiIdentityId string = apiIdentityResourceId
output apiIdentityClientId string = apiIdentityClientIdValue
output mcpIdentityId string = mcpIdentityResourceId
output mcpIdentityClientId string = mcpIdentityClientIdValue
output mcpIdentityPrincipalId string = mcpPrincipalId
output provisioningIdentityId string = provisioningIdentity.id
output provisioningIdentityClientId string = provisioningIdentity.properties.clientId
