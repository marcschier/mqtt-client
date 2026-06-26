# Tests

Seven projects backing the `Mqtt.Client` library. Run with `dotnet test` (where
applicable) or by invoking the test runner exe directly. A coverage script that
runs the unit + integration suites and merges results lives at
[`scripts/coverage.ps1`](../scripts/coverage.ps1).

## `Mqtt.Client.UnitTests/` — TUnit, multi-TFM

Fast, in-process tests on `net8.0` / `net9.0` / `net10.0`. **81 tests, ~76.6 % line
/ 58.3 % branch unit coverage** (combined with integration: **83.1 % / 65.2 %** —
see [`docs/coverage.md`](../docs/coverage.md)).

The `Fakes/` folder is the secret sauce:

- `FakePipeTransport` — implements `IMqttTransport` over a paired `Pipe`, so the
  client can be driven end-to-end with no real broker.
- `FakeBroker` — small DSL that consumes packets the client sent and emits canned
  responses (`SendConnAckAsync`, `SendPubAckAsync`, `SendPublishAsync`, etc.).

Test files cover: codec roundtrips and malformed inputs, topic-filter trie matching,
packet-id allocator, builder URI parsing, MqttClient connect/publish/subscribe via
the fake transport, in-memory persistence, exceptions/event args, DI
extensions, fuzz-finding regressions.

```bash
dotnet test tests/Mqtt.Client.UnitTests
# or directly:
./tests/Mqtt.Client.UnitTests/bin/Debug/net10.0/Mqtt.Client.UnitTests.exe --no-ansi --no-progress
```

In CI the unit suite is **also published with NativeAOT** (`net10.0`) and the native
binary is run as a trim/AOT gate: any new trim or AOT warning fails the build, and the
tests must pass when no JIT is available. `PublishAot` is scoped to `net10.0` in the
csproj, so normal `net8.0`/`net9.0`/`net10.0` build+run is unaffected.

```bash
dotnet publish tests/Mqtt.Client.UnitTests -c Release -f net10.0
./tests/Mqtt.Client.UnitTests/bin/Release/net10.0/<rid>/publish/Mqtt.Client.UnitTests --no-ansi --no-progress
```

## `Mqtt.Client.IntegrationTests/` — TUnit, net10.0

Spins up an in-process `MQTTnet.Server` on a random ephemeral port and exercises
the full publish/subscribe path across QoS 0/1/2.

```bash
./tests/Mqtt.Client.IntegrationTests/bin/Debug/net10.0/Mqtt.Client.IntegrationTests.exe
```

## `Mqtt.Client.Benchmarks/` — BenchmarkDotNet

Compares `Mqtt.Client` against `MQTTnet` 5.x with carefully aligned options
(MQTT 5, CleanStart, KeepAlive=60, etc.). Three codec micro-benchmarks plus five
end-to-end benchmarks: Publish QoS 0 / 1 / 2, Subscribe receive, Connect.
Uses an in-process MQTTnet broker for end-to-end runs (override with the
`MQTT_BENCH_BROKER=host:port` env var).

```bash
dotnet run -c Release --project tests/Mqtt.Client.Benchmarks -- --filter '*' --report
# add --full for DefaultJob (slower, higher fidelity)
```

`--report` regenerates `docs/benchmarks.md` and the README summary table via
`Reporting/SummaryGenerator.cs`. See [`docs/benchmarks.md`](../docs/benchmarks.md).

The `CrossLang/` folder adds a **cross-language throughput harness** (run with
`--crosslang`) that measures end-to-end publish→receive throughput for `Mqtt.Client`,
`MQTTnet`, the native-C `mosquitto_pub` CLI and a purpose-built `paho.mqtt.c` publisher
against a real Mosquitto broker, updating the **Cross-implementation throughput** section
of [`docs/benchmarks.md`](../docs/benchmarks.md). It skips when Mosquitto is not installed.

## `Mqtt.Client.InteropTests/` — TUnit, net10.0 (Mosquitto required)

Cross-implementation interop against a **real Eclipse Mosquitto broker** (the C MQTT
server) and the `mosquitto_pub` / `mosquitto_sub` C client tools: round-trips at QoS
0/1/2, our publish → C subscribe (retained), C publish → our subscribe, Last Will,
MQTT 5 properties, and TLS. A `MosquittoBroker` helper launches `mosquitto` on
ephemeral plain + TLS ports (self-signed cert generated in-process).

Every test **skips when `mosquitto` is not on PATH**, so the project is safe to build
and run anywhere — it only exercises real interop where Mosquitto is installed (the
`interop` CI job on Linux, or locally via Docker/WSL):

```bash
# locally (Docker; installs Mosquitto inside the container, leaves the host untouched):
pwsh scripts/interop.ps1            # interop tests + cross-language harness
pwsh scripts/interop.ps1 -Mode test

# on a host that already has Mosquitto + clients on PATH:
dotnet build tests/Mqtt.Client.InteropTests -c Release
./tests/Mqtt.Client.InteropTests/bin/Release/net10.0/Mqtt.Client.InteropTests --no-ansi --no-progress
```

## `Mqtt.Client.ApiTests/` — TUnit, multi-TFM

Public-API snapshot guard. Reflects over the exported types and members of
`Mqtt.Client` and compares them against `PublicApi.expected.txt`, failing on any
unintended change to the public surface (the minimum guardrail for intentional
semver-major changes). It lives in its own project because it relies on reflection and
is therefore **excluded from the NativeAOT-published unit suite**. After an intentional
API change, delete `PublicApi.expected.txt` and re-run to regenerate the baseline.

```bash
./tests/Mqtt.Client.ApiTests/bin/Release/net10.0/Mqtt.Client.ApiTests.exe --no-ansi --no-progress
```

## `Mqtt.Client.FuzzTests/` — SharpFuzz + libFuzzer (Linux)

Three harnesses dispatched by the first command-line argument:

- `decoder` — feeds random bytes through `MqttPacketDecoder.TryDecode`.
- `codec-roundtrip` — interprets random bytes as a structured publish; encodes →
  decodes → asserts equality.
- `topic-trie` — random filter/topic pairs through the trie matcher.

Crash inputs land in `findings/<harness>/`; a reproducer test in
`UnitTests/FuzzFindingsReproducerTests.cs` re-runs each crash input on every
unit-test run, pinning regressions forever.

```bash
# Linux (requires libfuzzer-dotnet — auto-downloaded by the script)
bash tests/Mqtt.Client.FuzzTests/scripts/run-fuzz.sh decoder 30
bash tests/Mqtt.Client.FuzzTests/scripts/run-fuzz.sh codec-roundtrip 60
```

The CI workflow `.github/workflows/fuzz.yml` is `workflow_dispatch`-only;
`harness` and `max_total_time` are inputs.

## `Mqtt.Client.ChaosTests/` — soak / chaos harness (net10.0 console)

A standalone soak app: standard publisher/subscriber workers run through an
in-process TCP chaos proxy in front of a real MQTTnet broker, while a seeded
fault scheduler injects network faults (drop, black-hole, latency, throttle,
corruption, refusal) and broker faults (restart, force-disconnect, reject).
Validators assert continuous recovery, no hangs (a publish **and** receive
liveness watchdog), and no memory/handle/thread leak (forced-GC sampling).
Supports `tcp`, `tls`, and `ws` transports. See [`docs/chaos.md`](../docs/chaos.md).

```bash
# 1-hour default; short reproducible local run shown here.
dotnet run --project tests/Mqtt.Client.ChaosTests -c Release -- \
  --duration 00:02:00 --transport tcp --seed 12345 --report ./chaos-report
```

The CI workflow `.github/workflows/chaos.yml` runs the full soak nightly (and on
demand), plus a short per-transport smoke on pull requests.

## Coverage

```bash
pwsh scripts/coverage.ps1
```
