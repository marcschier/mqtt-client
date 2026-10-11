<#
.SYNOPSIS
  Collects code coverage for the unit and integration test suites and merges them.
  Outputs cobertura XML under ./coverage/.

.EXAMPLE
  pwsh scripts/coverage.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Tfm = 'net10.0'
)
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repoRoot

$coverage = Join-Path $repoRoot 'coverage'
New-Item -ItemType Directory -Force -Path $coverage | Out-Null

Write-Host '==> Restoring pinned local tools...' -ForegroundColor Cyan
dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw 'Local tool restore failed.' }

Write-Host "==> Building solution ($Configuration)..." -ForegroundColor Cyan
dotnet build -c $Configuration --nologo -v quiet | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }

$unitExe = Join-Path $repoRoot "tests/Mqtt.Client.UnitTests/bin/$Configuration/$Tfm/Mqtt.Client.UnitTests.exe"
$intExe  = Join-Path $repoRoot "tests/Mqtt.Client.IntegrationTests/bin/$Configuration/$Tfm/Mqtt.Client.IntegrationTests.exe"

Write-Host "==> Collecting unit-test coverage..." -ForegroundColor Cyan
dotnet tool run dotnet-coverage -- collect `
  --output (Join-Path $coverage 'unit.cobertura.xml') `
  --output-format cobertura `
  --include-files (Join-Path (Split-Path $unitExe) 'Mqtt.Client.dll') `
  "$unitExe --no-ansi --no-progress" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Unit-test coverage collection failed.' }

Write-Host "==> Collecting integration-test coverage..." -ForegroundColor Cyan
dotnet tool run dotnet-coverage -- collect `
  --output (Join-Path $coverage 'integration.cobertura.xml') `
  --output-format cobertura `
  --include-files (Join-Path (Split-Path $intExe) 'Mqtt.Client.dll') `
  "$intExe --no-ansi --no-progress" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Integration-test coverage collection failed.' }

Write-Host "==> Merging..." -ForegroundColor Cyan
dotnet tool run dotnet-coverage -- merge `
  --output (Join-Path $coverage 'merged.cobertura.xml') `
  --output-format cobertura `
  (Join-Path $coverage 'unit.cobertura.xml') `
  (Join-Path $coverage 'integration.cobertura.xml') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Coverage merge failed.' }

Write-Host ''
Write-Host '==> Summary:' -ForegroundColor Green
foreach ($file in 'unit.cobertura.xml','integration.cobertura.xml','merged.cobertura.xml') {
    [xml]$c = Get-Content (Join-Path $coverage $file)
    $r = $c.coverage
    if (-not $r.SelectSingleNode('packages/package/classes/class/lines/line')) {
        throw "No instrumented lines found in $file."
    }
    $line = [math]::Round([double]$r.'line-rate' * 100, 1)
    $branch = [math]::Round([double]$r.'branch-rate' * 100, 1)
    "{0,-26} line={1,5}%  branch={2,5}%" -f $file, $line, $branch
}
