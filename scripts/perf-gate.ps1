<#
.SYNOPSIS
  Allocation-based performance regression gate for the codec benchmarks.

  Runs the EncodePublish / DecodePublish / EncodeSubscribe BenchmarkDotNet micro-benchmarks and
  compares each Mqtt.Client benchmark's *allocated bytes per operation* (a deterministic metric,
  unlike ShortRun timing) against a committed baseline. Fails when any benchmark allocates more than
  the baseline by more than -Tolerance. Timing is reported but never gated (too noisy in ShortRun).

.PARAMETER Update
  Overwrite the baseline file with the current measurements instead of comparing.

.PARAMETER Tolerance
  Allowed fractional allocation increase before failing (default 0.10 = 10%).

.EXAMPLE
  pwsh scripts/perf-gate.ps1 -Update     # capture/refresh the baseline
  pwsh scripts/perf-gate.ps1             # gate against the baseline (CI)
#>
[CmdletBinding()]
param(
    [switch]$Update,
    [double]$Tolerance = 0.10,
    [int]$AbsoluteSlackBytes = 96,
    [string]$BaselinePath = 'tests/Mqtt.Client.Benchmarks/perf-baseline.json'
)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repoRoot

$benchProj = 'tests/Mqtt.Client.Benchmarks'
$artifacts = Join-Path $benchProj 'BenchmarkDotNet.Artifacts/results'

Write-Host '==> Building benchmarks (Release)...' -ForegroundColor Cyan
dotnet build $benchProj -c Release --nologo -v quiet | Out-Null

if (Test-Path $artifacts) { Remove-Item -Recurse -Force $artifacts }

Write-Host '==> Running codec benchmarks...' -ForegroundColor Cyan
Push-Location $benchProj
try {
    dotnet run -c Release --no-build -- `
        --filter '*EncodePublish*' '*DecodePublish*' '*EncodeSubscribe*' | Out-Null
}
finally { Pop-Location }

# Collect allocated-bytes-per-op for every Mqtt.Client benchmark from the full JSON reports.
$current = [ordered]@{}
foreach ($json in Get-ChildItem -Recurse $artifacts -Filter '*-report-full.json') {
    $report = Get-Content $json.FullName -Raw | ConvertFrom-Json
    foreach ($b in $report.Benchmarks) {
        if ($b.Method -notlike '*MqttClient*') { continue }   # ours, not the MQTTnet baseline
        $type = ($b.Type -split '\.')[-1]
        $params = if ($b.Parameters) { "[$($b.Parameters)]" } else { '' }
        $key = "$type.$($b.Method)$params"
        $alloc = [long]$b.Memory.BytesAllocatedPerOperation
        $current[$key] = $alloc
    }
}

if ($current.Count -eq 0) {
    Write-Error 'No Mqtt.Client benchmark results found.'
    exit 2
}

if ($Update) {
    ($current | ConvertTo-Json) | Set-Content $BaselinePath
    Write-Host "==> Baseline written to $BaselinePath ($($current.Count) entries)." -ForegroundColor Green
    exit 0
}

if (-not (Test-Path $BaselinePath)) {
    Write-Error "Baseline not found at $BaselinePath. Run with -Update first."
    exit 2
}
$baseline = Get-Content $BaselinePath -Raw | ConvertFrom-Json

Write-Host ''
Write-Host '==> Allocation gate (bytes/op):' -ForegroundColor Green
$failed = $false
foreach ($key in $current.Keys) {
    $cur = $current[$key]
    $base = $baseline.$key
    if ($null -eq $base) {
        Write-Host ("  [new]  {0,-48} {1} B (no baseline)" -f $key, $cur) -ForegroundColor Yellow
        continue
    }
    # Allow the larger of a relative tolerance or a small absolute slack, so BenchmarkDotNet's
    # per-run allocation jitter (a tiny pooled-writer object that is sometimes elided) never trips
    # the gate, while a real regression (a new array/List = hundreds of bytes) still does.
    $limit = [math]::Max([math]::Ceiling($base * (1 + $Tolerance)), $base + $AbsoluteSlackBytes)
    if ($cur -gt $limit) {
        Write-Host ("  [FAIL] {0,-48} {1} B > {2} B (baseline {3} +{4:P0})" -f `
            $key, $cur, $limit, $base, $Tolerance) -ForegroundColor Red
        $failed = $true
    }
    else {
        Write-Host ("  [ok]   {0,-48} {1} B (baseline {2})" -f $key, $cur, $base)
    }
}

if ($failed) {
    Write-Error 'Allocation regression detected.'
    exit 1
}
Write-Host '==> No allocation regression.' -ForegroundColor Green
exit 0
