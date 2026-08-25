param(
    [string]$WorkspaceName = '',
    [string]$WorkspaceId = '',
    [string]$LakehouseName = '',
    [string]$LakehouseId = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($WorkspaceName)) { $WorkspaceName = $env:FABRIC_WORKSPACE_NAME }
if ([string]::IsNullOrWhiteSpace($WorkspaceId)) { $WorkspaceId = $env:FABRIC_WORKSPACE_ID }
if ([string]::IsNullOrWhiteSpace($LakehouseName)) { $LakehouseName = $env:FABRIC_LAKEHOUSE_NAME }
if ([string]::IsNullOrWhiteSpace($LakehouseId)) { $LakehouseId = $env:FABRIC_LAKEHOUSE_ID }

if ([string]::IsNullOrWhiteSpace($WorkspaceName) -and [string]::IsNullOrWhiteSpace($WorkspaceId)) {
    throw 'FABRIC_WORKSPACE_NAME or WorkspaceId is required.'
}
if ([string]::IsNullOrWhiteSpace($LakehouseName) -and [string]::IsNullOrWhiteSpace($LakehouseId)) {
    throw 'FABRIC_LAKEHOUSE_NAME or LakehouseId is required (set by Bicep fabricLakehouseName / deployment script env).'
}

function Get-FabricAccessToken {
    $resource = 'https://api.fabric.microsoft.com'

    if (-not [string]::IsNullOrWhiteSpace($env:AZURE_CLIENT_ID)) {
        try {
            $uri = "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=$resource&client_id=$($env:AZURE_CLIENT_ID)"
            $resp = Invoke-RestMethod -Uri $uri -Headers @{ Metadata = 'true' }
            if (-not [string]::IsNullOrWhiteSpace($resp.access_token)) {
                return $resp.access_token
            }
        }
        catch {
            Write-Verbose "IMDS token request failed: $_"
        }
    }

    try {
        return (Get-AzAccessToken -ResourceUrl $resource).Token
    }
    catch {}

    $token = az account get-access-token --resource $resource --query accessToken -o tsv 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($token)) {
        return $token
    }

    throw "Unable to acquire access token for '$resource' via IMDS, Az module, or az CLI."
}

function Invoke-FabricApi {
    param(
        [string]$Method,
        [string]$RelativeUrl,
        [object]$Body = $null
    )

    $uri = "https://api.fabric.microsoft.com/v1/$RelativeUrl"

    $token = Get-FabricAccessToken
    $headers = @{ Authorization = "Bearer $token" }

    if ($null -eq $Body) {
        return Invoke-RestMethod -Method $Method -Headers $headers -Uri $uri
    }

    $json = $Body | ConvertTo-Json -Depth 20
    $headers['Content-Type'] = 'application/json'
    return Invoke-RestMethod -Method $Method -Headers $headers -Uri $uri -Body $json
}

function Resolve-WorkspaceId {
    param(
        [string]$WorkspaceIdInput,
        [string]$WorkspaceNameInput
    )

    if (-not [string]::IsNullOrWhiteSpace($WorkspaceIdInput)) {
        $workspace = Invoke-FabricApi -Method 'GET' -RelativeUrl "workspaces/$WorkspaceIdInput"
        if (-not $workspace -or [string]::IsNullOrWhiteSpace([string]$workspace.id)) {
            throw "WorkspaceId '$WorkspaceIdInput' was not found in Fabric."
        }
        return [ordered]@{
            Id   = [string]$workspace.id
            Name = [string]$workspace.displayName
        }
    }

    if ([string]::IsNullOrWhiteSpace($WorkspaceNameInput)) {
        throw 'WorkspaceName or WorkspaceId is required.'
    }

    $workspaces = $null
    try {
        $workspaces = Invoke-FabricApi -Method 'GET' -RelativeUrl 'workspaces'
    }
    catch {
        $errorMessage = $_.Exception.Message
        if ($errorMessage -match 'Unauthorized|401') {
            throw @"
Unable to list Fabric workspaces (Unauthorized).
How to fix:
1) Use the correct tenant/subscription in Azure CLI: az account show
2) Ensure your identity has access to the target Fabric workspace (Member/Contributor/Admin)
3) If listing workspaces is blocked in your tenant, run this script with -WorkspaceId instead of -WorkspaceName

Current input:
- WorkspaceName: '$WorkspaceNameInput'
- WorkspaceId: '$WorkspaceIdInput'
"@
        }

        throw
    }
    $items = @()
    if ($workspaces -and $workspaces.value) {
        $items = @($workspaces.value)
    }

    $match = $items | Where-Object { $_.displayName -eq $WorkspaceNameInput } | Select-Object -First 1
    if (-not $match) {
        throw "Workspace '$WorkspaceNameInput' was not found in Fabric."
    }

    return [ordered]@{
        Id   = [string]$match.id
        Name = [string]$match.displayName
    }
}

function Get-LakehouseByName {
    param(
        [string]$ResolvedWorkspaceId,
        [string]$TargetLakehouseName
    )

    $items = Invoke-FabricApi -Method 'GET' -RelativeUrl "workspaces/$ResolvedWorkspaceId/items"
    $all = @()
    if ($items -and $items.value) {
        $all = @($items.value)
    }

    return $all |
        Where-Object { $_.type -eq 'Lakehouse' -and $_.displayName -eq $TargetLakehouseName } |
        Select-Object -First 1
}

function Wait-LakehouseReady {
    param(
        [string]$ResolvedWorkspaceId,
        [string]$ResolvedLakehouseId,
        [int]$MaxAttempts = 20,
        [int]$DelaySeconds = 5
    )

    for ($i = 1; $i -le $MaxAttempts; $i++) {
        $item = Invoke-FabricApi -Method 'GET' -RelativeUrl "workspaces/$ResolvedWorkspaceId/items/$ResolvedLakehouseId"
        if ($item) {
            return $item
        }

        Start-Sleep -Seconds $DelaySeconds
    }

    throw "Lakehouse '$ResolvedLakehouseId' did not become queryable after $MaxAttempts attempts."
}

function Ensure-Lakehouse {
    param(
        [string]$ResolvedWorkspaceId,
        [string]$TargetLakehouseId,
        [string]$TargetLakehouseName
    )

    if (-not [string]::IsNullOrWhiteSpace($TargetLakehouseId)) {
        $existing = Invoke-FabricApi -Method 'GET' -RelativeUrl "workspaces/$ResolvedWorkspaceId/items/$TargetLakehouseId"
        if (-not $existing) {
            throw "LakehouseId '$TargetLakehouseId' was not found in workspace '$ResolvedWorkspaceId'."
        }

        if ($existing.type -ne 'Lakehouse') {
            throw "Item '$TargetLakehouseId' is not a Lakehouse."
        }

        Write-Host "Using existing lakehouse by id: $($existing.displayName) ($TargetLakehouseId)"
        return [ordered]@{
            Id = [string]$existing.id
            Name = [string]$existing.displayName
        }
    }

    $byName = Get-LakehouseByName -ResolvedWorkspaceId $ResolvedWorkspaceId -TargetLakehouseName $TargetLakehouseName
    if ($byName) {
        Write-Host "Lakehouse already exists: $($byName.displayName) ($($byName.id))"
        return [ordered]@{
            Id = [string]$byName.id
            Name = [string]$byName.displayName
        }
    }

    Write-Host "Creating lakehouse '$TargetLakehouseName' in workspace '$ResolvedWorkspaceId'..."
    $created = Invoke-FabricApi -Method 'POST' -RelativeUrl "workspaces/$ResolvedWorkspaceId/items" -Body @{
        displayName = $TargetLakehouseName
        type = 'Lakehouse'
    }

    if (-not $created -or [string]::IsNullOrWhiteSpace([string]$created.id)) {
        throw 'Fabric API did not return a lakehouse id after create request.'
    }

    $ready = Wait-LakehouseReady -ResolvedWorkspaceId $ResolvedWorkspaceId -ResolvedLakehouseId $created.id

    return [ordered]@{
        Id = [string]$ready.id
        Name = [string]$ready.displayName
    }
}

Write-Host '=== Provision Fabric Lakehouse (R&D Knowledge) ==='
Write-Host "WorkspaceName: $WorkspaceName"
Write-Host "WorkspaceId (input): $WorkspaceId"
Write-Host "LakehouseName: $LakehouseName"
Write-Host "LakehouseId (input): $LakehouseId"

$resolvedWorkspace = Resolve-WorkspaceId -WorkspaceIdInput $WorkspaceId -WorkspaceNameInput $WorkspaceName
$resolvedWorkspaceId = $resolvedWorkspace.Id
$resolvedWorkspaceName = $resolvedWorkspace.Name
Write-Host "Resolved WorkspaceId: $resolvedWorkspaceId"
Write-Host "Resolved WorkspaceName: $resolvedWorkspaceName"

$lakehouse = Ensure-Lakehouse -ResolvedWorkspaceId $resolvedWorkspaceId -TargetLakehouseId $LakehouseId -TargetLakehouseName $LakehouseName
$resolvedLakehouseId = $lakehouse.Id

Write-Host "Resolved Lakehouse: $($lakehouse.Name) ($resolvedLakehouseId)"

$result = [ordered]@{
    WorkspaceId   = $resolvedWorkspaceId
    WorkspaceName = $resolvedWorkspaceName
    LakehouseId   = $resolvedLakehouseId
    LakehouseName = $lakehouse.Name
}

$resultFilePath = $env:PROVISION_RESULT_FILE
if (-not [string]::IsNullOrWhiteSpace($resultFilePath)) {
    $result | ConvertTo-Json -Depth 4 | Set-Content -Path $resultFilePath -Encoding UTF8
    Write-Host "Provision result written to $resultFilePath"
}

$DeploymentScriptOutputs = @{}
$DeploymentScriptOutputs['workspaceId']   = $resolvedWorkspaceId
$DeploymentScriptOutputs['workspaceName'] = $resolvedWorkspaceName
$DeploymentScriptOutputs['lakehouseId']   = $resolvedLakehouseId
$DeploymentScriptOutputs['lakehouseName'] = $lakehouse.Name

Write-Host 'Provision completed successfully.'
Write-Host "WorkspaceId: $resolvedWorkspaceId"
Write-Host "WorkspaceName: $resolvedWorkspaceName"
Write-Host "LakehouseId: $resolvedLakehouseId"

return $result
