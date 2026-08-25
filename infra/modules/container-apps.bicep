param location string
param resourceTags object
param containerAppsEnvironmentId string
param apiAppName string
param mcpAppName string
param frontendAppName string
param apiContainerImage string
param mcpContainerImage string
param frontendContainerImage string
param apiIdentityId string
param apiIdentityClientId string
param mcpIdentityId string
param mcpIdentityClientId string
param foundryProjectEndpoint string
param blobServiceUri string
param documentsContainerName string
param searchServiceEndpoint string
param embedDeploymentName string
param embedModelName string
param rerankDeploymentName string
param rerankModelName string
param embedEndpoint string
param rerankEndpoint string
param enableFabric bool = false
param fabricWorkspaceName string = ''
param fabricLakehouseName string = ''
@secure()
param applicationInsightsConnectionString string

var dataSourceMode = enableFabric ? 'Fabric' : 'Local'

var appInsightsEnv = [
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', secretRef: 'application-insights-connection-string' }
]

var mcpFoundryModelEnv = [
  { name: 'AzureFoundryModels__EmbedDeploymentName', value: embedDeploymentName }
  { name: 'AzureFoundryModels__RerankDeploymentName', value: rerankDeploymentName }
  { name: 'AzureFoundryModels__EmbedModelName', value: embedModelName }
  { name: 'AzureFoundryModels__RerankModelName', value: rerankModelName }
  { name: 'AzureFoundryModels__EmbedEndpoint', value: embedEndpoint }
  { name: 'AzureFoundryModels__RerankEndpoint', value: rerankEndpoint }
  { name: 'AzureFoundryModels__EmbeddingDimensions', value: '1024' }
  { name: 'AzureFoundryModels__EmbeddingBatchSize', value: '16' }
  { name: 'AzureFoundryModels__MaxConcurrentEmbeddingRequests', value: '1' }
  { name: 'AzureFoundryModels__MaxConcurrentRerankRequests', value: '2' }
]

var mcpContainerEnv = concat([
  { name: 'AzureSearch__Endpoint', value: searchServiceEndpoint }
  { name: 'AzureSearch__KnowledgeIndexName', value: 'rd-knowledge-evidence' }
  { name: 'AzureSearch__PolicyIndexName', value: 'rd-policy-knowledge' }
  { name: 'AzureSearch__VectorDimensions', value: '1024' }
  { name: 'PolicySeed__PolicyFilePath', value: '/app/policies/hls_policies.txt' }
  { name: 'DataSource__Mode', value: dataSourceMode }
  { name: 'DataSource__FabricLakehouse__WorkspaceName', value: fabricWorkspaceName }
  { name: 'DataSource__FabricLakehouse__LakehouseName', value: fabricLakehouseName }
  { name: 'AZURE_CLIENT_ID', value: mcpIdentityClientId }
], mcpFoundryModelEnv)

resource mcpApp 'Microsoft.App/containerApps@2025-01-01' = {
  name: mcpAppName
  location: location
  tags: resourceTags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${mcpIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironmentId
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
      secrets: [
        {
          name: 'application-insights-connection-string'
          value: applicationInsightsConnectionString
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'mcp'
          image: mcpContainerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: concat(mcpContainerEnv, appInsightsEnv, [
            { name: 'AZURE_STORAGE_BLOB_SERVICE_URI', value: blobServiceUri }
            { name: 'AzureStorage__ContainerName', value: documentsContainerName }
            { name: 'AzureStorage__NormalizedPrefix', value: 'normalized' }
            { name: 'McpStartup__EnsureSearchIndexesOnStartup', value: 'true' }
            { name: 'McpStartup__SeedPoliciesOnStartup', value: 'false' }
          ])
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 15
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

resource apiApp 'Microsoft.App/containerApps@2025-01-01' = {
  name: apiAppName
  location: location
  tags: resourceTags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${apiIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironmentId
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
      secrets: [
        {
          name: 'application-insights-connection-string'
          value: applicationInsightsConnectionString
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiContainerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: concat([
            { name: 'AZURE_FOUNDRY_PROJECT_ENDPOINT', value: foundryProjectEndpoint }
            { name: 'AZURE_STORAGE_BLOB_SERVICE_URI', value: blobServiceUri }
            { name: 'AzureSearch__Endpoint', value: searchServiceEndpoint }
            { name: 'AzureSearch__KnowledgeIndexName', value: 'rd-knowledge-evidence' }
            { name: 'AzureSearch__PolicyIndexName', value: 'rd-policy-knowledge' }
            { name: 'AzureSearch__VectorDimensions', value: '1024' }
            { name: 'AzureStorage__ContainerName', value: documentsContainerName }
            { name: 'AzureStorage__NormalizedPrefix', value: 'normalized' }
            { name: 'AzureFoundryModels__EmbedDeploymentName', value: embedDeploymentName }
            { name: 'AzureFoundryModels__RerankDeploymentName', value: rerankDeploymentName }
            { name: 'AzureFoundryModels__EmbedModelName', value: embedModelName }
            { name: 'AzureFoundryModels__RerankModelName', value: rerankModelName }
            { name: 'AzureFoundryModels__EmbedEndpoint', value: embedEndpoint }
            { name: 'AzureFoundryModels__RerankEndpoint', value: rerankEndpoint }
            { name: 'AzureFoundryModels__EmbeddingDimensions', value: '1024' }
            { name: 'AzureFoundryModels__EmbeddingBatchSize', value: '16' }
            { name: 'AzureFoundryModels__MaxConcurrentEmbeddingRequests', value: '1' }
            { name: 'AzureFoundryModels__MaxConcurrentRerankRequests', value: '2' }
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'AZURE_CLIENT_ID', value: apiIdentityClientId }
            { name: 'DataSource__Mode', value: dataSourceMode }
            { name: 'DataSource__FabricLakehouse__WorkspaceName', value: fabricWorkspaceName }
            { name: 'DataSource__FabricLakehouse__LakehouseName', value: fabricLakehouseName }
          ], appInsightsEnv)
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 5
              periodSeconds: 15
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

resource frontendApp 'Microsoft.App/containerApps@2025-01-01' = {
  name: frontendAppName
  location: location
  tags: resourceTags
  properties: {
    managedEnvironmentId: containerAppsEnvironmentId
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
    }
    template: {
      containers: [
        {
          name: 'frontend'
          image: frontendContainerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ApiBaseUrl', value: 'https://${apiApp.properties.configuration.ingress.fqdn}/' }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/'
                port: 8080
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/'
                port: 8080
              }
              initialDelaySeconds: 5
              periodSeconds: 15
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

output apiUrl string = uri('https://${apiApp.properties.configuration.ingress.fqdn}', '')
output frontendUrl string = uri('https://${frontendApp.properties.configuration.ingress.fqdn}', '')
output mcpUrl string = uri('https://${mcpApp.properties.configuration.ingress.fqdn}', '')
output mcpFqdn string = mcpApp.properties.configuration.ingress.fqdn
output mcpContainerEnv array = mcpContainerEnv
