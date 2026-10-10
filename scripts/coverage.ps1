<#
.SYNOPSIS
  Runs the unit-test projects with coverage and builds an HTML + summary report.
.DESCRIPTION
  Domain.UnitTests and Application.UnitTests need no Docker. Output goes to coverage-report/ (git-ignored).
.EXAMPLE
  ./scripts/coverage.ps1
  ./scripts/coverage.ps1 -Threshold 80   # fail if line coverage is below 80%
#>
param(
    [int]$Threshold = 0
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $results = Join-Path $root 'artifacts/coverage-raw'
    if (Test-Path $results) { Remove-Item $results -Recurse -Force }

    dotnet tool restore | Out-Null

    foreach ($project in 'tests/Domain.UnitTests', 'tests/Application.UnitTests') {
        dotnet test $project --settings coverage.runsettings --results-directory $results
        if ($LASTEXITCODE -ne 0) { throw "Tests failed in $project" }
    }

    $reports = (Get-ChildItem $results -Recurse -Filter coverage.cobertura.xml | ForEach-Object FullName) -join ';'
    dotnet tool run reportgenerator "-reports:$reports" "-targetdir:coverage-report" "-reporttypes:Html;TextSummary;Cobertura"

    $summary = Get-Content (Join-Path $root 'coverage-report/Summary.txt')
    $summary | Write-Host

    if ($Threshold -gt 0) {
        $line = ($summary | Select-String 'Line coverage:\s*([\d\.]+)%').Matches[0].Groups[1].Value
        if ([double]$line -lt $Threshold) { throw "Line coverage $line% is below threshold $Threshold%" }
    }
}
finally {
    Pop-Location
}
