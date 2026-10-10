<#
.SYNOPSIS
  Contract tests: 1) consumer tests write the pacts, 2) MS4 (provider) verifies the MS3 pact.
.DESCRIPTION
  Consumer step needs no Docker. Provider step starts Postgres through Aspire, so Docker must be running.
  Pact files are written to ./pacts and are committed so reviewers and the provider build can read them.
.EXAMPLE
  ./scripts/run-pact.ps1
  ./scripts/run-pact.ps1 -ConsumerOnly
#>
param(
    [switch]$ConsumerOnly
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $env:PACT_DO_NOT_TRACK = 'true'

    dotnet test tests/Contract.Consumer.Tests
    if ($LASTEXITCODE -ne 0) { throw 'Consumer pact tests failed' }

    Get-ChildItem pacts -Filter *.json | ForEach-Object { Write-Host "pact written: pacts/$($_.Name)" }

    if (-not $ConsumerOnly) {
        dotnet test tests/Contract.Provider.Tests
        if ($LASTEXITCODE -ne 0) { throw 'Provider verification failed' }
    }
}
finally {
    Pop-Location
}
