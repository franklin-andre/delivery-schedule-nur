<#
.SYNOPSIS
  Runs the Postman collection (happy + error flows) headlessly with newman against the docker-compose stack.
.DESCRIPTION
  Requires Docker and Node.js (newman is fetched via npx). Brings the stack up (rebuilding the API image),
  runs src/Api/MS4-Delivery-Calendar.postman_collection.json, then leaves the stack running.
.EXAMPLE
  ./scripts/run-newman.ps1
  ./scripts/run-newman.ps1 -BaseUrl http://localhost:5000 -NoCompose   # target an already running API
#>
param(
    [string]$BaseUrl = 'http://localhost:8080',
    [switch]$NoCompose
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if (-not $NoCompose) {
        # docker writes progress to stderr; don't let that abort the script under 'Stop'
        $ErrorActionPreference = 'Continue'
        docker compose up -d --build
        $ErrorActionPreference = 'Stop'
        if ($LASTEXITCODE -ne 0) { throw 'docker compose up failed' }
    }

    # Wait until the API answers (any HTTP status means it is up)
    $ready = $false
    for ($i = 0; $i -lt 30 -and -not $ready; $i++) {
        try { Invoke-WebRequest "$BaseUrl/api/v1/calendars/1" -UseBasicParsing -TimeoutSec 2 | Out-Null; $ready = $true }
        catch { if ($_.Exception.Response) { $ready = $true } else { Start-Sleep -Seconds 2 } }
    }
    if (-not $ready) { throw "API not reachable at $BaseUrl" }

    $ErrorActionPreference = 'Continue'
    npx --yes newman run src/Api/MS4-Delivery-Calendar.postman_collection.json --env-var "baseUrl=$BaseUrl" --reporters cli
    if ($LASTEXITCODE -ne 0) { throw 'newman reported failures' }
}
finally {
    Pop-Location
}
