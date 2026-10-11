#!/usr/bin/env bash
# Copyright (c) 2026 marcschier. Licensed under the MIT License.
#
# Run a SharpFuzz harness via libfuzzer-dotnet.
#
# Usage:
#   ./scripts/run-fuzz.sh <harness> [max_total_time_seconds]
#
#   harness                = decoder | codec-roundtrip | topic-trie
#   max_total_time_seconds = libFuzzer -max_total_time (default 30)
#
# Downloads a pinned libfuzzer-dotnet on first run, instruments Mqtt.Client.dll
# with the SharpFuzz dotnet tool, then drives the harness.
set -euo pipefail

HARNESS="${1:-decoder}"
MAX_TIME="${2:-30}"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJ_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$PROJ_DIR/../.." && pwd)"
TOOLS_DIR="$PROJ_DIR/.tools"
CORPUS="$PROJ_DIR/corpus/$HARNESS"
FINDINGS="$PROJ_DIR/findings/$HARNESS"
mkdir -p "$TOOLS_DIR" "$CORPUS" "$FINDINGS"

# 1. Build (Release) and instrument Mqtt.Client.dll with SharpFuzz.
echo "==> Restoring + building Mqtt.Client.FuzzTests (Release)..."
dotnet build "$PROJ_DIR/Mqtt.Client.FuzzTests.csproj" -c Release -nologo -v quiet >/dev/null
OUT_DIR="$PROJ_DIR/bin/Release/net10.0"
TARGET_DLL="$OUT_DIR/Mqtt.Client.dll"

echo "==> Restoring pinned local tools..."
dotnet tool restore --tool-manifest "$REPO_ROOT/.config/dotnet-tools.json"

echo "==> Instrumenting $(basename "$TARGET_DLL")..."
dotnet tool run sharpfuzz -- "$TARGET_DLL" >/dev/null

# 2. Ensure libfuzzer-dotnet driver exists.
LIBFUZZER="$TOOLS_DIR/libfuzzer-dotnet"
if [ ! -x "$LIBFUZZER" ]; then
  echo "==> Downloading libfuzzer-dotnet driver (pinned)..."
  URL="https://github.com/Metalnem/libfuzzer-dotnet/releases/download/v2025.05.02.0904/libfuzzer-dotnet-ubuntu"
  curl -fsSL "$URL" -o "$LIBFUZZER"
  chmod +x "$LIBFUZZER"
fi

# 3. Run.
echo "==> Running $HARNESS for $MAX_TIME seconds..."
HARNESS_NAME="$HARNESS" "$LIBFUZZER" \
  --target_path="$OUT_DIR/Mqtt.Client.FuzzTests" \
  --target_arg="$HARNESS" \
  -max_total_time="$MAX_TIME" \
  -artifact_prefix="$FINDINGS/" \
  "$CORPUS"
