<#
.SYNOPSIS
  Runs the cross-implementation interop tests and/or the cross-language throughput harness against a
  real Eclipse Mosquitto broker inside a .NET SDK Docker container, so Mosquitto installs without
  touching the host. Mirrors the `interop` CI job.

.DESCRIPTION
  The interop tests and the `--crosslang` benchmark skip automatically when Mosquitto is not on PATH,
  so they are inert on a normal Windows dev box. This script provides a real Linux environment with
  Mosquitto + the mosquitto_pub/mosquitto_sub C clients to exercise them. Requires Docker.

  Note: the container builds Linux binaries into the mounted bin/obj; rebuild on Windows afterward if
  you need the Windows outputs.

.EXAMPLE
  pwsh scripts/interop.ps1              # interop tests + cross-language harness
  pwsh scripts/interop.ps1 -Mode test  # interop tests only
  pwsh scripts/interop.ps1 -Mode bench # cross-language throughput harness only
#>
[CmdletBinding()]
param(
    [ValidateSet('test', 'bench', 'both')]
    [string]$Mode = 'both',
    [string]$Image = 'mcr.microsoft.com/dotnet/sdk:10.0'
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/..").Path

$runTest = if ($Mode -in 'test', 'both') { '1' } else { '0' }
$runBench = if ($Mode -in 'bench', 'both') { '1' } else { '0' }

$bash = @'
set -e
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq mosquitto mosquitto-clients libpaho-mqtt-dev gcc >/dev/null
export PATH="/usr/sbin:$PATH"
cd /repo
if [ "__RUN_TEST__" = "1" ]; then
  echo "=== interop tests ==="
  dotnet build tests/Mqtt.Client.InteropTests/Mqtt.Client.InteropTests.csproj -c Release -m:1
  ./tests/Mqtt.Client.InteropTests/bin/Release/net10.0/Mqtt.Client.InteropTests \
    --no-ansi --no-progress
fi
if [ "__RUN_BENCH__" = "1" ]; then
  echo "=== building the Paho C publisher harness ==="
  gcc -O2 -Wall -o /tmp/paho_pub_bench \
    tests/Mqtt.Client.Benchmarks/CrossLang/native/paho_pub_bench.c -lpaho-mqtt3c
  export PAHO_PUB_BENCH=/tmp/paho_pub_bench
  echo "=== cross-language throughput harness ==="
  dotnet build tests/Mqtt.Client.Benchmarks/Mqtt.Client.Benchmarks.csproj -c Release -m:1
  dotnet exec tests/Mqtt.Client.Benchmarks/bin/Release/net10.0/Mqtt.Client.Benchmarks.dll --crosslang
fi
'@

$bash = $bash.Replace('__RUN_TEST__', $runTest).Replace('__RUN_BENCH__', $runBench)
# bash requires LF line endings.
$bash = $bash.Replace("`r`n", "`n")

Write-Host "Running interop ($Mode) in $Image ..." -ForegroundColor Cyan
docker run --rm -v "${repo}:/repo" -w /repo $Image bash -lc $bash
exit $LASTEXITCODE
