param location string
param resourceTags object
param storageAccountName string
param documentsContainerName string
param searchServiceName string
param searchSku string

resource storageAccount 'Microsoft.Storage/storageAccounts@2025-01-01' = {
  name: storageAccountName
  location: location
  // The default Container Apps environment is not VNet-integrated, so Blob must
  // remain reachable through its OAuth-authenticated public endpoint.
  tags: union(resourceTags, {
    SecurityControl: 'Ignore'
  })
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
    supportsHttpsTrafficOnly: true
    publicNetworkAccess: 'Enabled'
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2025-01-01' = {
  parent: storageAccount
  name: 'default'
}

resource documentsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = {
  parent: blobService
  name: documentsContainerName
  properties: {
    publicAccess: 'None'
  }
}

resource searchService 'Microsoft.Search/searchServices@2025-05-01' = {
  name: searchServiceName
  location: location
  tags: resourceTags
  sku: {
    name: searchSku
  }
  properties: {
    replicaCount: 1
    partitionCount: 1
    hostingMode: 'Default'
    publicNetworkAccess: 'enabled'
    authOptions: {
      aadOrApiKey: {
        aadAuthFailureMode: 'http401WithBearerChallenge'
      }
    }
  }
}

output storageAccountName string = storageAccount.name
output storageAccountId string = storageAccount.id
output blobServiceUri string = storageAccount.properties.primaryEndpoints.blob
output documentsContainerName string = documentsContainerName
output searchServiceName string = searchService.name
output searchServiceId string = searchService.id
output searchServiceEndpoint string = 'https://${searchService.name}.search.windows.net'
