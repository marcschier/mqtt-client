# Cross-implementation throughput — Mqtt.Client vs MQTTnet vs C (Mosquitto, Paho)

_Generated 2026-06-25 16:32 UTC._

End-to-end **publish→receive** throughput: each publisher sends N messages through a real [Eclipse Mosquitto](https://mosquitto.org/) broker to a constant `mosquitto_sub -C N` subscriber; the rate is N divided by the wall-clock time for the subscriber to receive all N. Two native-C datapoints are included — the `mosquitto_pub` CLI tool and a purpose-built **paho.mqtt.c** publisher. Higher is better.

These numbers are **wall-clock and cross-language** — not directly comparable to the per-operation [BenchmarkDotNet results](benchmarks.md). The **Mosquitto C (CLI)** column is the `mosquitto_pub` command-line tool (line-buffered stdin, no QoS 1 pipelining): a convenience tool, not a throughput-optimised client. The **Paho C (lib)** column is a purpose-built publisher on the Eclipse Paho C synchronous `MQTTClient` v5 API doing exactly what the .NET clients do — one persistent connection over the same broker — so it is the true apples-to-apples native baseline. For QoS 1 all three persistent publishers pipeline up to 100 acknowledgements in flight (sustained throughput, not per-message round-trip latency); QoS 0 is measured end-to-end (the subscriber must receive all N), so fire-and-forget enqueue is not mistaken for delivery.

**Reading the Paho C column:** its QoS 0 figure (no acknowledgements) is competitive, but its QoS 1 figure is held back by paho.mqtt.c exposing no `TCP_NODELAY` option — its acknowledgement round-trips stall on Nagle / delayed-ACK even when pipelined, where the .NET clients disable Nagle. So the QoS 1 number reflects paho's default TCP behaviour, not a native-C ceiling — a useful reminder that TCP tuning, not language, dominates QoS 1 throughput here.

## QoS 0 — throughput (msg/s, higher is better)

| Payload | Mqtt.Client | MQTTnet | Mosquitto C (CLI) | Paho C (lib) |
| --- | ---: | ---: | ---: | ---: |
| 64 B | 58,625 | 26,561 | 13,800 | 21,872 |
| 256 B | 56,953 | 30,784 | 23,604 | 16,530 |
| 1 KiB | 55,102 | 53,338 | 8,757 | 14,608 |
| 16 KiB | 21,126 | 14,390 | 3,297 | 1,116 |
| 64 KiB | 6,689 | 4,933 | 1,381 | 280 |

## QoS 1 — throughput (msg/s, higher is better)

| Payload | Mqtt.Client | MQTTnet | Mosquitto C (CLI) | Paho C (lib) |
| --- | ---: | ---: | ---: | ---: |
| 64 B | 2,850 | 2,839 | 13,248 | 950 |
| 256 B | 2,866 | 2,404 | 11,372 | 950 |
| 1 KiB | 2,700 | 2,786 | 9,576 | 935 |
| 16 KiB | 2,295 | 170 | 3,158 | 867 |
| 64 KiB | 2,021 | 183 | 787 | 325 |

