# Microsoft Fabric setup (optional)

Fabric integration is **optional**. The default deployment uses bundled `dataset-seed` assets (`enableFabric=false`) and does not require Fabric. Follow this guide only if you want MCP tools and API Start ingestion to read raw sources from Fabric OneLake.

## Prerequisites

Before you begin, confirm you have:

- An Azure subscription where you can **create resource groups** and **user-assigned managed identities**.
- Azure CLI (`az`) installed and logged in (`az login`), or an equivalent Az PowerShell session.
- A **Microsoft Fabric workspace** that is capacity-backed and accessible to your account.
- Permission to **add / assign workspace roles** in that Fabric workspace (the setup script assigns the **Contributor** role to the managed identity).
- Permission to **create and deploy Azure resources** into the subscription and resource group used by this sample.

## Steps

### 1. Pre-provision the Fabric identity

From the repository root, run:

```powershell
.\infra\scripts\setup-fabric-provision-identity.ps1 -ResourceGroupName <resource-group-name> -WorkspaceName <fabric-workspace-name>
```

Optional parameters:

- `-IdentityName` — defaults to `fabric-identity`
- `-Location` — used when creating a new resource group (default `westus3`)

This script:

1. Creates the resource group if it does not exist (or reuses it).
2. Creates a user-assigned managed identity in that resource group.
3. Assigns the Fabric workspace **Contributor** role to that identity.

Note the identity name printed by the script (`fabricIdentityName`). You will need it at deploy time.

When `enableFabric=true`, deploy mounts this identity on **both** the API and MCP Container Apps (`AZURE_CLIENT_ID`) and does **not** create separate `api-identity` / `mcp-identity` UAMIs. The same principal already has Fabric workspace Contributor (from this script) plus Storage / Search / Foundry RBAC from Bicep.

### 2. Deploy to Azure using the same resource group

1. Click **Deploy to Azure** (see [Quick deploy](../README.md#quick-deploy)).
2. Select the **same subscription and resource group** created or reused in step 1.
3. Set `enableFabric` to `true`.
4. Set `fabricWorkspaceName` to the Fabric workspace used in step 1.
5. Set `fabricIdentityName` to the identity from step 1 (default: `fabric-identity`).
6. Complete the remaining model and region parameters, then wait for deployment to finish.

For all parameters, see [deployment-parameters.md](./deployment-parameters.md).
