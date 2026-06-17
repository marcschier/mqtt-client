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

if (-not (Get-Command dotnet-coverage -ErrorAction SilentlyContinue)) {
    Write-Host '==> Installing dotnet-coverage global tool...' -ForegroundColor Cyan
    dotnet tool install --global dotnet-coverage | Out-Null
}

Write-Host "==> Building solution ($Configuration)..." -ForegroundColor Cyan
dotnet build -c $Configuration --nologo -v quiet | Out-Null

$unitExe = Join-Path $repoRoot "tests/Mqtt.Client.UnitTests/bin/$Configuration/$Tfm/Mqtt.Client.UnitTests.exe"
$intExe  = Join-Path $repoRoot "tests/Mqtt.Client.IntegrationTests/bin/$Configuration/$Tfm/Mqtt.Client.IntegrationTests.exe"

Write-Host "==> Collecting unit-test coverage..." -ForegroundColor Cyan
dotnet-coverage collect `
  --output (Join-Path $coverage 'unit.cobertura.xml') `
  --output-format cobertura `
  --include-files 'src/Mqtt.Client/*' `
  "$unitExe --no-ansi --no-progress" | Out-Null

Write-Host "==> Collecting integration-test coverage..." -ForegroundColor Cyan
dotnet-coverage collect `
  --output (Join-Path $coverage 'integration.cobertura.xml') `
  --output-format cobertura `
  --include-files 'src/Mqtt.Client/*' `
  "$intExe --no-ansi --no-progress" | Out-Null

Write-Host "==> Merging..." -ForegroundColor Cyan
dotnet-coverage merge `
  --output (Join-Path $coverage 'merged.cobertura.xml') `
  --output-format cobertura `
  (Join-Path $coverage 'unit.cobertura.xml') `
  (Join-Path $coverage 'integration.cobertura.xml') | Out-Null

Write-Host ''
Write-Host '==> Summary:' -ForegroundColor Green
foreach ($file in 'unit.cobertura.xml','integration.cobertura.xml','merged.cobertura.xml') {
    [xml]$c = Get-Content (Join-Path $coverage $file)
    $r = $c.coverage
    $line = [math]::Round([double]$r.'line-rate' * 100, 1)
    $branch = [math]::Round([double]$r.'branch-rate' * 100, 1)
    "{0,-26} line={1,5}% ({2}/{3})  branch={4,5}% ({5}/{6})" -f `
        $file, $line, $r.'lines-covered', $r.'lines-valid', $branch, $r.'branches-covered', $r.'branches-valid'
}
