# Contributing to Mqtt.Client

Thanks for your interest. Bug reports, feature requests, docs improvements and code PRs are
all welcome.

## Quick start

You need the .NET 10 SDK (pinned in `global.json`). Older SDKs won't pick the right tooling.

```bash
git clone https://github.com/marcschier/mqtt-client
cd mqtt-client
dotnet restore
dotnet build
```

## Running tests

```bash
# All unit tests on all TFMs
dotnet build tests/Mqtt.Client.UnitTests -c Release
./tests/Mqtt.Client.UnitTests/bin/Release/net10.0/Mqtt.Client.UnitTests   # 81+ tests

# Integration tests (uses in-process MQTTnet broker, net10 only)
./tests/Mqtt.Client.IntegrationTests/bin/Release/net10.0/Mqtt.Client.IntegrationTests

# NativeAOT gate (publishes the unit suite with PublishAot=true and runs the native binary)
dotnet publish tests/Mqtt.Client.UnitTests -c Release -f net10.0
```

## Coverage

```bash
pwsh scripts/coverage.ps1
```

Outputs cobertura XML to `coverage/`. Bar: **unit ≥ 80 %, combined ≥ 80 %**; see
[`docs/coverage.md`](docs/coverage.md).

## Benchmarks

```bash
dotnet run -c Release --project tests/Mqtt.Client.Benchmarks -- --filter '*' --report
# add --full for DefaultJob (slower, higher fidelity)
```

`--report` regenerates `docs/benchmarks.md` and the README summary table. See
[`docs/benchmarks.md`](docs/benchmarks.md) for the matrix vs MQTTnet 5.x.

## Fuzzing (Linux / WSL)

```bash
bash tests/Mqtt.Client.FuzzTests/scripts/run-fuzz.sh decoder 30
bash tests/Mqtt.Client.FuzzTests/scripts/run-fuzz.sh codec-roundtrip 60
bash tests/Mqtt.Client.FuzzTests/scripts/run-fuzz.sh topic-trie 30
```

Crash inputs land in `tests/Mqtt.Client.FuzzTests/findings/<harness>/` and are
auto-replayed by `FuzzFindingsReproducerTests` on every unit-test run.

## Code style

- All `.cs` files carry the single-line SPDX header (run `pwsh scripts/add-headers.ps1` if
  you add a new file).
- Roslynator rules at error severity (see `.editorconfig`); only RCS1140/1141/1142/1181/1194/1261
  are intentionally suppressed (see comments in `.editorconfig`).
- `Meziantou.Analyzer`, `IDisposableAnalyzers`, .NET CA-rules all on; `IsAotCompatible=true`
  enforced on `net10.0`.

## Pull requests

- Branch from `main`. Conventional-Commits-style commit subjects are nice but not required.
- Run `dotnet build` and the unit suite before pushing.
- For perf-sensitive changes, re-run `dotnet run --project tests/Mqtt.Client.Benchmarks`
  and include the before/after in the PR description.
- For changes that touch the public API, expect the PR to update `docs/conformance.md`
  and (post-v1.0) the public-API snapshot.

## Reporting security issues

**Please don't open a public issue for security reports.** Use GitHub Security Advisories
(privately): [report a vulnerability](https://github.com/marcschier/mqtt-client/security/advisories/new).
See [`SECURITY.md`](SECURITY.md) for the full policy and the threat model in
[`docs/security-audit.md`](docs/security-audit.md).

## Releases

`Nerdbank.GitVersioning` computes the package version from `version.json` + git height.
See [`docs/releasing.md`](docs/releasing.md) for the cut-a-release flow.
