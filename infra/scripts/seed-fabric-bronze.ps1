param(
    [Parameter(Mandatory = $true)]
    [string]$WorkspaceId,

    [Parameter(Mandatory = $true)]
    [string]$LakehouseId,

    [Parameter(Mandatory = $true)]
    [string]$CasesPath,

    [string]$OneLakeEndpoint = 'https://onelake.dfs.fabric.microsoft.com',

    [int]$MaxRetries = 5,

    [int]$RetryDelaySeconds = 3
)

$ErrorActionPreference = 'Stop'

function Get-OneLakeAccessToken {
    $resource = 'https://storage.azure.com'

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

    throw "Unable to acquire access token for '$resource'."
}

function Invoke-OneLakeUpload {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseUri,
        [Parameter(Mandatory = $true)]
        [byte[]]$FileBytes,
        [Parameter(Mandatory = $true)]
        [string]$BearerToken,
        [Parameter(Mandatory = $true)]
        [string]$DisplayPath,
        [int]$Retries = 5,
        [int]$DelaySeconds = 3
    )

    $lastError = $null
    for ($attempt = 1; $attempt -le $Retries; $attempt++) {
        $handler = [System.Net.Http.HttpClientHandler]::new()
        $client = [System.Net.Http.HttpClient]::new($handler)
        try {
            $bearer = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $BearerToken)

            $create = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Put, "$BaseUri`?resource=file")
            $create.Headers.Authorization = $bearer
            $resp = $client.SendAsync($create).GetAwaiter().GetResult()
            if (-not $resp.IsSuccessStatusCode -and [int]$resp.StatusCode -ne 409) {
                $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                throw "Create failed for '$DisplayPath' ($($resp.StatusCode)): $body"
            }

            # OneLake rejects append with Content-Length: 0. Empty files: create + flush@0 only.
            if ($FileBytes.LongLength -gt 0) {
                $content = [System.Net.Http.ByteArrayContent]::new($FileBytes)
                $content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('application/octet-stream')
                $content.Headers.ContentLength = $FileBytes.LongLength

                $append = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Patch, "$BaseUri`?action=append&position=0")
                $append.Headers.Authorization = $bearer
                $append.Content = $content
                $resp = $client.SendAsync($append).GetAwaiter().GetResult()
                if (-not $resp.IsSuccessStatusCode) {
                    $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                    throw "Append failed for '$DisplayPath' ($($resp.StatusCode)): $body"
                }
            }

            $flush = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Patch, "$BaseUri`?action=flush&position=$($FileBytes.LongLength)")
            $flush.Headers.Authorization = $bearer
            $resp = $client.SendAsync($flush).GetAwaiter().GetResult()
            if (-not $resp.IsSuccessStatusCode) {
                $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                throw "Flush failed for '$DisplayPath' ($($resp.StatusCode)): $body"
            }

            return
        }
        catch {
            $lastError = $_
            if ($attempt -lt $Retries) {
                Write-Host "Transient upload failure for '$DisplayPath' (attempt $attempt/$Retries): $($_.Exception.Message). Retrying in ${DelaySeconds}s..."
                Start-Sleep -Seconds $DelaySeconds
                continue
            }
        }
        finally {
            $client.Dispose()
            $handler.Dispose()
        }
    }

    throw "Upload failed for '$DisplayPath' after $Retries attempts: $($lastError.Exception.Message)"
}

Write-Host '=== Fabric bronze seed (cases/*/ingest) ==='
Write-Host "WorkspaceId: $WorkspaceId"
Write-Host "LakehouseId: $LakehouseId"
Write-Host "CasesPath: $CasesPath"
Write-Host "OneLake endpoint: $OneLakeEndpoint"
Write-Host 'Target layout: Files/bronze/cases/{caseId}/ingest/{file}'

$casesRoot = (Resolve-Path -LiteralPath $CasesPath).ProviderPath
$caseFolders = Get-ChildItem -Path $casesRoot -Directory | Sort-Object -Property Name
if ($caseFolders.Count -eq 0) {
    throw "No case folders found in $casesRoot"
}

$allFiles = @()
foreach ($case in $caseFolders) {
    $ingestPath = Join-Path $case.FullName 'ingest'
    if (-not (Test-Path -LiteralPath $ingestPath)) {
        Write-Warning "Skipping case '$($case.Name)' — no ingest/ folder"
        continue
    }

    $caseFiles = Get-ChildItem -Path $ingestPath -File | Sort-Object -Property Name
    foreach ($f in $caseFiles) {
        $allFiles += [pscustomobject]@{
            FullName     = $f.FullName
            CaseName     = $case.Name
            RelativePath = "cases/$($case.Name)/ingest/$($f.Name)"
        }
    }
}

if ($allFiles.Count -eq 0) {
    throw "No ingest files found under $casesRoot/*/ingest"
}

Write-Host "Found $($allFiles.Count) ingest file(s) across case folders."

$token = Get-OneLakeAccessToken

if ($PSVersionTable.PSVersion.Major -ge 7) {
    $allFiles | ForEach-Object -ThrottleLimit 10 -Parallel {
        $file = $_
        $wsId = $using:WorkspaceId
        $lhId = $using:LakehouseId
        $endpoint = $using:OneLakeEndpoint
        $token = $using:token
        $retries = $using:MaxRetries
        $delay = $using:RetryDelaySeconds

        $targetPath = "$lhId/Files/bronze/$($file.RelativePath)"
        $baseUri = "$endpoint/$wsId/$targetPath"
        $fileBytes = [System.IO.File]::ReadAllBytes($file.FullName)

        $handler = [System.Net.Http.HttpClientHandler]::new()
        $client = [System.Net.Http.HttpClient]::new($handler)
        $lastError = $null
        for ($attempt = 1; $attempt -le $retries; $attempt++) {
            try {
                $bearer = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $token)

                $create = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Put, "$baseUri`?resource=file")
                $create.Headers.Authorization = $bearer
                $resp = $client.SendAsync($create).GetAwaiter().GetResult()
                if (-not $resp.IsSuccessStatusCode -and [int]$resp.StatusCode -ne 409) {
                    $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                    throw "Create failed for '$($file.RelativePath)' ($($resp.StatusCode)): $body"
                }

                # OneLake rejects append with Content-Length: 0. Empty files: create + flush@0 only.
                if ($fileBytes.LongLength -gt 0) {
                    $content = [System.Net.Http.ByteArrayContent]::new($fileBytes)
                    $content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('application/octet-stream')
                    $content.Headers.ContentLength = $fileBytes.LongLength

                    $append = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Patch, "$baseUri`?action=append&position=0")
                    $append.Headers.Authorization = $bearer
                    $append.Content = $content
                    $resp = $client.SendAsync($append).GetAwaiter().GetResult()
                    if (-not $resp.IsSuccessStatusCode) {
                        $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                        throw "Append failed for '$($file.RelativePath)' ($($resp.StatusCode)): $body"
                    }
                }

                $flush = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Patch, "$baseUri`?action=flush&position=$($fileBytes.LongLength)")
                $flush.Headers.Authorization = $bearer
                $resp = $client.SendAsync($flush).GetAwaiter().GetResult()
                if (-not $resp.IsSuccessStatusCode) {
                    $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                    throw "Flush failed for '$($file.RelativePath)' ($($resp.StatusCode)): $body"
                }

                Write-Host "Uploaded: Files/bronze/$($file.RelativePath)"
                $lastError = $null
                break
            }
            catch {
                $lastError = $_
                if ($attempt -lt $retries) {
                    Write-Host "Transient upload failure for '$($file.RelativePath)' (attempt $attempt/$retries). Retrying in ${delay}s..."
                    Start-Sleep -Seconds $delay
                }
            }
        }

        $client.Dispose()
        $handler.Dispose()

        if ($null -ne $lastError) {
            throw "Upload failed for '$($file.FullName)': $($lastError.Exception.Message) [URI: $baseUri]"
        }
    }
}
else {
    Write-Host 'PowerShell 5.x detected. Uploading bronze files sequentially (parallel mode requires PowerShell 7+).'

    foreach ($file in $allFiles) {
        $targetPath = "$LakehouseId/Files/bronze/$($file.RelativePath)"
        $baseUri = "$OneLakeEndpoint/$WorkspaceId/$targetPath"
        $fileBytes = [System.IO.File]::ReadAllBytes($file.FullName)

        Invoke-OneLakeUpload `
            -BaseUri $baseUri `
            -FileBytes $fileBytes `
            -BearerToken $token `
            -DisplayPath $file.RelativePath `
            -Retries $MaxRetries `
            -DelaySeconds $RetryDelaySeconds

        Write-Host "Uploaded: Files/bronze/$($file.RelativePath)"
    }
}

Write-Host "Bronze upload completed. Files uploaded: $($allFiles.Count)"
exit 0
