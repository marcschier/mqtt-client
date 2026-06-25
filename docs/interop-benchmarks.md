# Cross-implementation throughput — Mqtt.Client vs MQTTnet vs Mosquitto C

_Generated 2026-06-25 07:50 UTC._

End-to-end **publish→receive** throughput: each publisher sends N messages through a real [Eclipse Mosquitto](https://mosquitto.org/) broker to a constant `mosquitto_sub -C N` subscriber; the rate is N divided by the wall-clock time for the subscriber to receive all N. The native-C datapoint is Mosquitto's own `mosquitto_pub` (libmosquitto). Higher is better.

These numbers are **wall-clock and cross-language** — not directly comparable to the per-operation [BenchmarkDotNet results](benchmarks.md). The native-C datapoint is the `mosquitto_pub` **command-line tool** (libmosquitto): a convenience CLI that reads messages line-by-line from stdin and does not pipeline QoS 1, so it trails the persistent, tight-loop .NET publishers here. This measures that tool, **not** the ceiling of a C library driven directly (e.g. paho.mqtt.c with batching). Both .NET clients await acknowledgements for QoS 1; QoS 0 is measured end-to-end (the subscriber must receive all N), so fire-and-forget enqueue is not mistaken for delivery.

## QoS 0 — throughput (msg/s, higher is better)

| Payload | Mqtt.Client | MQTTnet | Mosquitto C |
| --- | ---: | ---: | ---: |
| 64 B | 62,845 | 37,071 | 14,158 |
| 256 B | 60,948 | 40,061 | 13,864 |
| 1 KiB | 53,140 | 22,291 | 9,096 |
| 16 KiB | 16,218 | 15,727 | 3,144 |
| 64 KiB | 6,272 | 4,888 | 1,425 |

## QoS 1 — throughput (msg/s, higher is better)

| Payload | Mqtt.Client | MQTTnet | Mosquitto C |
| --- | ---: | ---: | ---: |
| 64 B | 12,487 | 10,535 | 10,750 |
| 256 B | 15,239 | 15,033 | 10,636 |
| 1 KiB | 13,096 | 14,156 | 9,379 |
| 16 KiB | 9,142 | 11,171 | 3,129 |
| 64 KiB | 5,031 | 4,647 | 774 |

