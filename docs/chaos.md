# Chaos / soak testing

`tests/Mqtt.Client.ChaosTests` is a standalone console harness that runs a standard publishing/subscribing client under sustained network and broker chaos to prove the client recovers continuously and stays stable — no hangs, no memory/handle/thread leak. It is the ruggedization gate: a nightly 1‑hour run (configurable) plus a short per‑transport smoke on pull requests.

## What it does

A configurable number of workers each connect (through an in‑process TCP **chaos proxy**) to a real in‑process **MQTTnet broker**, subscribe to their own topic space, and continuously publish an incrementing sequence number cycling QoS 0/1/2. Because the broker echoes a worker's own publishes back, every healthy worker also continuously receives — so a stalled *receive* clock (for example a subscription that was never restored after a reconnect) is detectable independently of the *publish* clock.

A deterministic, seeded **fault scheduler** alternates healthy windows with injected faults:

- Network (via the proxy, beneath TLS/WebSocket framing): hard connection drop, black‑hole / half‑open (stop forwarding without closing), added latency + jitter, bandwidth throttling, byte corruption, truncation, and connection refusal.
- Broker: restart (drops session state, so a reconnect reports session‑present=false and exercises auto‑resubscribe), force‑disconnect of all clients, and a connection‑reject window.

Every fault is followed by a recovery grace period and then a healthy window during which the harness asserts the workload has fully recovered.

## What it validates

- **Continuous recovery** — at least one reconnect occurred and the workload resumed after every fault.
- **No hangs** — during healthy windows every worker must make forward progress on *both* its publish and receive clocks within a bound; a permanent stall fails the run.
- **No leaks** — every few seconds the harness forces a full GC and samples the managed heap, working set, handle count, and thread count; after a warmup it flags unbounded growth (band + linear‑regression slope).
- **No logical leaks** — at quiescence the pending‑ack and outbound‑queue‑depth gauges must return to ~0.

A run exits non‑zero on any violation and writes `chaos-metrics.csv` (the client's metrics over time) and `chaos-memory.csv` (the post‑GC resource samples) to the `--report` directory.

## Running it

```bash
# Default: 1 hour, TCP, 4 workers, all faults, random seed.
dotnet run --project tests/Mqtt.Client.ChaosTests -c Release

# A short local soak over TLS, reproducible by seed.
dotnet run --project tests/Mqtt.Client.ChaosTests -c Release -- \
  --duration 00:02:00 --transport tls --clients 3 --seed 12345 --report ./chaos-report
```

### Options

| Flag | Default | Meaning |
| --- | --- | --- |
| `--duration` | `01:00:00` | `hh:mm:ss` or a plain seconds count. |
| `--transport` | `tcp` | `tcp`, `tls`, or `ws`. |
| `--clients` | `4` | Number of publisher/subscriber workers. |
| `--scenario` | `all` | A single fault name (e.g. `broker-restart`) or `all`. |
| `--seed` | random | RNG seed; the chosen seed is logged so a failing run reproduces deterministically. |
| `--keepalive` | `3` | Client keep‑alive seconds (kept short so the black‑hole fault exercises the read‑idle watchdog). |
| `--report` | `.` | Output directory for the CSV reports. |
| `--fail-fast` | off | Stop at the first violation. |

## Reproducing a nightly failure

The harness prints `*** SEED=<n> ***` at startup and the failing run's artifacts include it. Re‑run with the same `--seed`, `--transport`, `--clients`, and `--scenario` to replay the exact fault sequence.

## CI

`.github/workflows/chaos.yml` runs the full soak nightly (and on demand via *Run workflow*, where duration/transport/seed/clients/scenario are inputs), and a short ~3‑minute smoke per transport on pull requests that touch the client or the harness. All jobs fail on a violation and upload the CSV reports and console log as artifacts.

## Observability

The harness asserts on the same `System.Diagnostics.Metrics` instruments the library publishes on the `Mqtt.Client` meter, which were added/extended for this work: connect attempts/failures/duration, recovery duration, disconnects (tagged by reason), resubscribes, keep‑alive pings/timeouts, dropped messages, decode errors, and pull‑based gauges for connection state, pending acks, in‑flight publishes, outbound queue depth, and subscription count. See [advanced.md](advanced.md) for the full list.
