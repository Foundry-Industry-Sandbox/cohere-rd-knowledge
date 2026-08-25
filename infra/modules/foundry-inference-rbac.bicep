param foundryAccountName string
param principalId string

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: foundryAccountName
}

var cognitiveServicesOpenAIUserRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
)

resource foundryOpenAIUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccount.id, principalId, cognitiveServicesOpenAIUserRoleDefinitionId)
  scope: foundryAccount
  properties: {
    roleDefinitionId: cognitiveServicesOpenAIUserRoleDefinitionId
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
