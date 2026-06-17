# Copyright (c) 2026 marcschier. Licensed under the MIT License.
#
# Windows stub for run-fuzz.sh.
#
# SharpFuzz + libfuzzer-dotnet require Linux ELF binaries; running natively on
# Windows is not supported. Use WSL or the manually-triggered CI workflow
# (.github/workflows/fuzz.yml) instead.
Write-Host 'Fuzzing requires Linux. Either:' -ForegroundColor Yellow
Write-Host '  1) Run inside WSL:'
Write-Host '       wsl bash tests/Mqtt.Client.FuzzTests/scripts/run-fuzz.sh decoder 30'
Write-Host '  2) Trigger the workflow via GitHub Actions:'
Write-Host '       gh workflow run fuzz.yml'
exit 1
