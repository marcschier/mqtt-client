<#
.SYNOPSIS
  Idempotently adds a single-line SPDX-style copyright header to every .cs file
  under src/ and tests/. Skips obj/, bin/, and files that already carry the header.

.EXAMPLE
  pwsh scripts/add-headers.ps1
#>
[CmdletBinding()]
param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$Holder = 'marcschier',
    [int]$Year = (Get-Date).Year
)

$header = "// Copyright (c) $Year $Holder. Licensed under the MIT License."
$marker = 'Licensed under the MIT License.'

$files = Get-ChildItem -Path $Root -Recurse -Include *.cs -File |
    Where-Object { $_.FullName -notmatch '[\\/](obj|bin|artifacts|BenchmarkDotNet\.Artifacts)[\\/]' }

$updated = 0
$skipped = 0
foreach ($f in $files) {
    $lines = Get-Content -LiteralPath $f.FullName
    if ($lines.Length -gt 0 -and ($lines[0..([Math]::Min(3, $lines.Length - 1))] -join "`n").Contains($marker)) {
        $skipped++
        continue
    }
    $newContent = ($header, '') + $lines
    Set-Content -LiteralPath $f.FullName -Value $newContent -Encoding utf8
    $updated++
}
Write-Host "Headers: $updated updated, $skipped already had a header." -ForegroundColor Green
