# Cross-implementation throughput — Mqtt.Client vs MQTTnet vs Mosquitto C

_Generated 2026-06-25 06:56 UTC._

End-to-end **publish→receive** throughput: each publisher sends N messages through a real [Eclipse Mosquitto](https://mosquitto.org/) broker to a constant `mosquitto_sub -C N` subscriber; the rate is N divided by the wall-clock time for the subscriber to receive all N. The native-C datapoint is Mosquitto's own `mosquitto_pub` (libmosquitto). Higher is better.

These numbers are **wall-clock and cross-language** — not directly comparable to the per-operation [BenchmarkDotNet results](benchmarks.md). A native-C client is expected to lead; the value here is the order-of-magnitude datapoint against a real broker on the same host. Both .NET clients await acknowledgements for QoS 1; QoS 0 is measured end-to-end (the subscriber must receive all N), so fire-and-forget enqueue is not mistaken for delivery.

## QoS 0 — throughput (msg/s, higher is better)

| Payload | Mqtt.Client | MQTTnet | Mosquitto C |
| --- | ---: | ---: | ---: |
| 64 B | 37,972 | 19,694 | 12,940 |
| 256 B | 32,711 | 31,722 | 13,598 |
| 1 KiB | 18,577 | 25,314 | 7,523 |
| 16 KiB | 1,034 | 2,797 | 2,538 |
| 64 KiB | 938 | 618 | 488 |

## QoS 1 — throughput (msg/s, higher is better)

| Payload | Mqtt.Client | MQTTnet | Mosquitto C |
| --- | ---: | ---: | ---: |
| 64 B | 2,545 | 1,826 | 7,203 |
| 256 B | 2,436 | 2,145 | 8,897 |
| 1 KiB | 2,289 | 2,132 | 4,443 |
| 16 KiB | 2,012 | 2,415 | 2,444 |
| 64 KiB | 950 | 635 | 587 |

