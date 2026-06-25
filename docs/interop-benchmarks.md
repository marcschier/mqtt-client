# Cross-implementation throughput — Mqtt.Client vs MQTTnet vs C (Mosquitto, Paho)

_Generated 2026-06-25 10:04 UTC._

End-to-end **publish→receive** throughput: each publisher sends N messages through a real [Eclipse Mosquitto](https://mosquitto.org/) broker to a constant `mosquitto_sub -C N` subscriber; the rate is N divided by the wall-clock time for the subscriber to receive all N. Two native-C datapoints are included — the `mosquitto_pub` CLI tool and a purpose-built **paho.mqtt.c** publisher. Higher is better.

These numbers are **wall-clock and cross-language** — not directly comparable to the per-operation [BenchmarkDotNet results](benchmarks.md). The **Mosquitto C (CLI)** column is the `mosquitto_pub` command-line tool (line-buffered stdin, no QoS 1 pipelining): a convenience tool, not a throughput-optimised client. The **Paho C (lib)** column is a purpose-built publisher on the Eclipse Paho C synchronous `MQTTClient` v5 API doing exactly what the .NET clients do — one persistent connection over the same broker — so it is the true apples-to-apples native baseline. For QoS 1 all three persistent publishers pipeline up to 100 acknowledgements in flight (sustained throughput, not per-message round-trip latency); QoS 0 is measured end-to-end (the subscriber must receive all N), so fire-and-forget enqueue is not mistaken for delivery.

**Reading the Paho C column:** its QoS 0 figure (no acknowledgements) is competitive, but its QoS 1 figure is held back by paho.mqtt.c exposing no `TCP_NODELAY` option — its acknowledgement round-trips stall on Nagle / delayed-ACK even when pipelined, where the .NET clients disable Nagle. So the QoS 1 number reflects paho's default TCP behaviour, not a native-C ceiling — a useful reminder that TCP tuning, not language, dominates QoS 1 throughput here.

## QoS 0 — throughput (msg/s, higher is better)

| Payload | Mqtt.Client | MQTTnet | Mosquitto C (CLI) | Paho C (lib) |
| --- | ---: | ---: | ---: | ---: |
| 64 B | 16,827 | 18,302 | 13,807 | 14,118 |
| 256 B | 40,205 | 21,622 | 12,719 | 12,574 |
| 1 KiB | 22,944 | 10,511 | 6,673 | 13,387 |
| 16 KiB | 6,729 | 3,415 | 2,355 | 2,083 |
| 64 KiB | 775 | 1,042 | 596 | 596 |

## QoS 1 — throughput (msg/s, higher is better)

| Payload | Mqtt.Client | MQTTnet | Mosquitto C (CLI) | Paho C (lib) |
| --- | ---: | ---: | ---: | ---: |
| 64 B | 2,707 | 2,584 | 5,069 | 948 |
| 256 B | 2,671 | 2,323 | 8,444 | 948 |
| 1 KiB | 2,529 | 2,111 | 4,625 | 932 |
| 16 KiB | 2,327 | 250 | 1,868 | 865 |
| 64 KiB | 1,948 | 123 | 563 | 528 |

