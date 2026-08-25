param location string
param resourceTags object
param deploymentSuffix string
param deploymentScriptIdentityName string
param policySeedIdentityId string
param policySeedIdentityClientId string
param policySeedEmbedEndpoint string
param policySeedEmbedModelName string
param policySeedEmbeddingDimensions int
param policySeedJobName string
param provisioningJobName string

resource deploymentScriptIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: deploymentScriptIdentityName
  location: location
  tags: resourceTags
}

resource deploymentScriptContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, deploymentScriptIdentity.id, 'Contributor', deploymentSuffix)
  scope: resourceGroup()
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b24988ac-6180-42a0-ab88-20f7382dd24c')
    principalId: deploymentScriptIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

var postDeployScript = '''
  set -euo pipefail

  wait_for_policy_seed_foundry_access() {
    local probe_url="${POLICY_SEED_EMBED_ENDPOINT%/}/embeddings?api-version=2024-05-01-preview"
    local probe_body
    local error_file="/tmp/policy-seed-foundry-probe-error"
    probe_body=$(printf '{"model":"%s","input":["rbac-readiness-probe"],"dimensions":%s}' \
      "${POLICY_SEED_EMBED_MODEL_NAME}" \
      "${POLICY_SEED_EMBEDDING_DIMENSIONS}")

    echo "Authenticating with the policy-seed managed identity..."
    az login --identity --username "${POLICY_SEED_CLIENT_ID}" --allow-no-subscriptions >/dev/null
    az account set --subscription "${AZURE_SUBSCRIPTION_ID}"

    echo "Waiting for policy-seed Foundry data-plane access..."
    for attempt in $(seq 1 80); do
      if az rest \
        --method post \
        --url "${probe_url}" \
        --resource "https://ai.azure.com" \
        --body "${probe_body}" \
        >/dev/null 2>"${error_file}"; then
        rm -f "${error_file}"
        echo "Policy-seed Foundry data-plane access is ready."
        return 0
      fi

      if grep -Eqi 'PermissionDenied|AuthorizationFailed|Unauthorized|Forbidden|(^|[^0-9])(401|403)([^0-9]|$)' "${error_file}"; then
        echo "Policy-seed Foundry role is not effective yet (attempt ${attempt}/80); retrying in 15 seconds..."
        sleep 15
        continue
      fi

      echo "Foundry readiness probe failed with a non-RBAC error:" >&2
      cat "${error_file}" >&2
      rm -f "${error_file}"
      return 1
    done

    echo "Timed out waiting for policy-seed Foundry data-plane access." >&2
    cat "${error_file}" >&2
    rm -f "${error_file}"
    return 1
  }

  run_job() {
    local job_name="$1"
    local container_name="$2"
    local display_name="$3"

    echo "Starting ${display_name} job..."
    local execution
    execution=$(az containerapp job start \
      --name "${job_name}" \
      --resource-group "${RESOURCE_GROUP}" \
      --query name -o tsv)
    echo "${display_name} job execution: ${execution}"

    for i in $(seq 1 120); do
      local status
      status=$(az containerapp job execution show \
        --name "${job_name}" \
        --resource-group "${RESOURCE_GROUP}" \
        --job-execution-name "${execution}" \
        --query properties.status -o tsv)

      echo "${display_name} job status: ${status}"

      if [ "${status}" = "Succeeded" ]; then
        echo "${display_name} completed successfully."
        return 0
      fi

      if [ "${status}" = "Failed" ]; then
        echo "${display_name} failed. Fetching recent job logs..."
        az containerapp job logs show \
          --name "${job_name}" \
          --resource-group "${RESOURCE_GROUP}" \
          --execution "${execution}" \
          --container "${container_name}" \
          --tail 50 || true
        return 1
      fi

      sleep 15
    done

    echo "Timed out waiting for ${display_name}."
    return 1
  }

  wait_for_policy_seed_foundry_access
  az account clear

  echo "Authenticating to Azure with the post-deploy managed identity..."
  az login --identity --username "${POST_DEPLOY_CLIENT_ID}" --allow-no-subscriptions >/dev/null
  az account set --subscription "${AZURE_SUBSCRIPTION_ID}"
  az extension add --name containerapp --upgrade --yes

  run_job "${POLICY_SEED_JOB_NAME}" "policy-seed" "Policy seed"

  echo "Waiting briefly before agent provisioning..."
  sleep 60
  run_job "${PROVISIONING_JOB_NAME}" "agent-provisioning" "Agent provisioning"

  echo "Post-deploy jobs completed successfully."
'''

// deploymentScripts always stage content through shared-key storage. ACI embeds the command
// in the resource payload and authenticates every Azure CLI call with the user-assigned identity.
resource postDeployRunner 'Microsoft.ContainerInstance/containerGroups@2023-05-01' = {
  name: 'run-post-deploy-${deploymentSuffix}'
  location: location
  tags: resourceTags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${deploymentScriptIdentity.id}': {}
      '${policySeedIdentityId}': {}
    }
  }
  properties: {
    osType: 'Linux'
    restartPolicy: 'Never'
    containers: [
      {
        name: 'post-deploy-runner'
        properties: {
          image: 'mcr.microsoft.com/azure-cli:2.62.0'
          command: [
            '/bin/bash'
            '-c'
            postDeployScript
          ]
          environmentVariables: [
            {
              name: 'POST_DEPLOY_CLIENT_ID'
              value: deploymentScriptIdentity.properties.clientId
            }
            {
              name: 'POLICY_SEED_CLIENT_ID'
              value: policySeedIdentityClientId
            }
            {
              name: 'POLICY_SEED_EMBED_ENDPOINT'
              value: policySeedEmbedEndpoint
            }
            {
              name: 'POLICY_SEED_EMBED_MODEL_NAME'
              value: policySeedEmbedModelName
            }
            {
              name: 'POLICY_SEED_EMBEDDING_DIMENSIONS'
              value: string(policySeedEmbeddingDimensions)
            }
            {
              name: 'AZURE_SUBSCRIPTION_ID'
              value: subscription().subscriptionId
            }
            {
              name: 'RESOURCE_GROUP'
              value: resourceGroup().name
            }
            {
              name: 'POLICY_SEED_JOB_NAME'
              value: policySeedJobName
            }
            {
              name: 'PROVISIONING_JOB_NAME'
              value: provisioningJobName
            }
          ]
          resources: {
            requests: {
              cpu: 1
              memoryInGB: json('1.5')
            }
          }
        }
      }
    ]
  }
  dependsOn: [
    deploymentScriptContributorRole
  ]
}
