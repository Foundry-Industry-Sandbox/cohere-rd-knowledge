$ErrorActionPreference = 'Stop'

$envWorkspaceName = $env:FABRIC_WORKSPACE_NAME
$envLakehouseName = $env:FABRIC_LAKEHOUSE_NAME
$envWorkspaceId = $env:FABRIC_WORKSPACE_ID
$envLakehouseId = $env:FABRIC_LAKEHOUSE_ID
$envResourceGroupName = $env:RESOURCE_GROUP_NAME
$envRepositoryArchiveUrl = $env:REPOSITORY_ARCHIVE_URL
$envGithubToken = $env:GITHUB_TOKEN

if ([string]::IsNullOrWhiteSpace($envWorkspaceName)) {
    throw 'FABRIC_WORKSPACE_NAME is required (set by Bicep runFabricSeed environment variables).'
}
if ([string]::IsNullOrWhiteSpace($envLakehouseName)) {
    throw 'FABRIC_LAKEHOUSE_NAME is required (set by Bicep runFabricSeed environment variables).'
}
if ([string]::IsNullOrWhiteSpace($envResourceGroupName)) {
    throw 'RESOURCE_GROUP_NAME is required.'
}
if ([string]::IsNullOrWhiteSpace($envRepositoryArchiveUrl)) {
    throw 'REPOSITORY_ARCHIVE_URL is required (set by Bicep runFabricSeed from fabricRepositoryArchiveUrl).'
}

function Get-ScratchRoot {
    foreach ($candidate in @($env:TEMP, $env:TMPDIR, '/tmp')) {
        if (-not [string]::IsNullOrWhiteSpace($candidate)) {
            return $candidate
        }
    }
    return '/tmp'
}

function Expand-ArchiveCrossPlatform {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ArchivePath,
        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    try {
        Expand-Archive -LiteralPath $ArchivePath -DestinationPath $DestinationPath -Force
        return
    }
    catch {
        Write-Host "Expand-Archive failed, trying unzip: $($_.Exception.Message)"
    }

    $unzip = Get-Command unzip -ErrorAction SilentlyContinue
    if (-not $unzip) {
        throw 'Could not extract repository archive: Expand-Archive failed and unzip is unavailable.'
    }

    & $unzip.Source -o $ArchivePath -d $DestinationPath
    if ($LASTEXITCODE -ne 0) {
        throw "unzip failed with exit code $LASTEXITCODE"
    }
}

Write-Host '=== Fabric Data Seed (R&D Knowledge) ==='
Write-Host "WorkspaceName: $envWorkspaceName"
Write-Host "LakehouseName: $envLakehouseName"
Write-Host "ResourceGroupName: $envResourceGroupName"
Write-Host "RepositoryArchiveUrl: $envRepositoryArchiveUrl"

$workDir = Join-Path (Get-ScratchRoot) 'rdknowledge-fabric-seed'
if (Test-Path -LiteralPath $workDir) {
    Remove-Item -LiteralPath $workDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $workDir | Out-Null

$archivePath = Join-Path $workDir 'repo.zip'
Write-Host "Downloading repository archive: $envRepositoryArchiveUrl"
$downloadHeaders = @{
    # GitHub returns 404 for many clients without a User-Agent (including Azure deployment scripts).
    'User-Agent' = 'cohere-rd-knowledge-fabric-seed'
}
if (-not [string]::IsNullOrWhiteSpace($envGithubToken)) {
    $downloadHeaders['Authorization'] = "Bearer $envGithubToken"
}
try {
    Invoke-WebRequest -Uri $envRepositoryArchiveUrl -Headers $downloadHeaders -OutFile $archivePath -UseBasicParsing
}
catch {
    $status = $null
    if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
        $status = [int]$_.Exception.Response.StatusCode
    }
    throw @"
Failed to download Fabric seed archive$(if ($status) { " (HTTP $status)" } else { '' }).
URL: $envRepositoryArchiveUrl
$($_.Exception.Message)

How to fix:
1) Set fabricRepositoryArchiveUrl to a public zip that contains dataset-seed/cases and infra/scripts
   (default: https://github.com/foundry-industry-sandbox/cohere-rd-knowledge/archive/refs/heads/main.zip).
2) If the repo is private, pass a token via GITHUB_TOKEN on the deployment script.
3) Prefer the codeload URL if github.com/archive redirects fail from Azure:
   https://codeload.github.com/foundryindustrysandbox/cohere-rd-knowledge/zip/refs/heads/main
"@
}
Write-Host 'Extracting repository archive...'
Expand-ArchiveCrossPlatform -ArchivePath $archivePath -DestinationPath $workDir

$repoRoot = Get-ChildItem $workDir -Directory | Where-Object {
    (Test-Path (Join-Path $_.FullName 'infra/scripts') -PathType Container) -and
    (Test-Path (Join-Path $_.FullName 'dataset-seed/cases') -PathType Container)
} | Select-Object -First 1

if (-not $repoRoot) {
    if ((Test-Path (Join-Path $workDir 'infra/scripts') -PathType Container) -and
        (Test-Path (Join-Path $workDir 'dataset-seed/cases') -PathType Container)) {
        $repoRoot = Get-Item $workDir
    }
}

if (-not $repoRoot) {
    throw "Could not locate extracted repository root. Expected either a wrapper directory containing both 'infra/scripts' and 'dataset-seed/cases', or those two directories at the top level of the archive."
}

$scriptsDirectory = Join-Path $repoRoot.FullName 'infra/scripts'

if ([string]::IsNullOrWhiteSpace($envWorkspaceId) -or [string]::IsNullOrWhiteSpace($envLakehouseId)) {
    Write-Host 'Pre-resolving Fabric lakehouse from workspace name...'
    try {
        $provisionResult = & (Join-Path $scriptsDirectory 'provision-fabric-lakehouse.ps1') `
            -WorkspaceName $envWorkspaceName `
            -LakehouseName $envLakehouseName
    }
    catch {
        throw "provision-fabric-lakehouse.ps1 threw: $($_.Exception.Message)`n$($_.ScriptStackTrace)"
    }
    if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "provision-fabric-lakehouse.ps1 failed with exit code $LASTEXITCODE"
    }

    $envWorkspaceId = [string]$provisionResult.WorkspaceId
    $envLakehouseId = [string]$provisionResult.LakehouseId
}

if ([string]::IsNullOrWhiteSpace($envWorkspaceId) -or [string]::IsNullOrWhiteSpace($envLakehouseId)) {
    throw 'Could not resolve a valid workspaceId and lakehouseId.'
}

Write-Host "Resolved WorkspaceId: $envWorkspaceId"
Write-Host "Resolved LakehouseId: $envLakehouseId"

$casesPath = Join-Path $repoRoot.FullName 'dataset-seed/cases'
if (-not (Test-Path -LiteralPath $casesPath)) {
    throw "Cases path not found: $casesPath"
}

Write-Host "Running seed-fabric-bronze.ps1 with cases: $casesPath"
$bronzeScript = Join-Path $scriptsDirectory 'seed-fabric-bronze.ps1'
& $bronzeScript `
    -WorkspaceId $envWorkspaceId `
    -LakehouseId $envLakehouseId `
    -CasesPath $casesPath
if ($LASTEXITCODE -ne 0) {
    throw "seed-fabric-bronze.ps1 failed with exit code $LASTEXITCODE"
}

$DeploymentScriptOutputs = @{}
$DeploymentScriptOutputs['workspaceId'] = $envWorkspaceId
$DeploymentScriptOutputs['lakehouseId'] = $envLakehouseId
$DeploymentScriptOutputs['lakehouseName'] = $envLakehouseName

Write-Host 'Fabric data seed completed successfully.'
Write-Host "WorkspaceId: $envWorkspaceId"
Write-Host "LakehouseId: $envLakehouseId"
