# Mqtt.Client vs MQTTnet — benchmark results

_Generated 2026-06-22 20:44 UTC._

[MQTTnet](https://github.com/dotnet/MQTTnet) is a mature, battle-tested .NET MQTT library. These benchmarks are not a verdict on MQTTnet — they exist to make tradeoffs visible for callers choosing between the two clients. See the README's "When to pick MQTTnet instead" section for guidance.

Each section below opens with a one-line note on what that benchmark measures. They fall into two groups: **codec micro-benchmarks** (in-memory encode/decode — no broker, no network) and **end-to-end benchmarks** (a real in-process MQTTnet broker over a TCP loopback, exercising the full client stack per operation). In every table the MQTTnet row is the baseline (Ratio = 1.00), and `PayloadSize` is the MQTT payload length in bytes.

Run with:
```
dotnet run -c Release --project tests/Mqtt.Client.Benchmarks -- --filter '*' --report
dotnet run -c Release --project tests/Mqtt.Client.Benchmarks -- --filter '*' --full --report
```

## Mqtt.Client.Benchmarks.ConnectLatencyBenchmark-report-github

**End-to-end.** Measures one full connect + disconnect cycle — TCP handshake, CONNECT/CONNACK, then DISCONNECT — creating a brand-new client per invocation so the complete handshake cost is captured. It runs with a small, fixed invocation count so neither client exhausts the OS ephemeral-port range (which would surface as `SocketException 10048`); the figure is handshake latency, not throughput. There is no payload-size parameter.

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]   : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  InvocationCount=16  IterationCount=10  
LaunchCount=1  UnrollFactor=1  WarmupCount=3  

```
| Method      | Mean     | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------ |---------:|----------:|----------:|------:|--------:|----------:|------------:|
| MQTTnet     | 3.548 ms | 0.3180 ms | 0.1893 ms |  1.00 |    0.07 |  38.64 KB |        1.00 |
| Mqtt.Client | 4.043 ms | 0.3479 ms | 0.2070 ms |  1.14 |    0.08 |  33.86 KB |        0.88 |

## Mqtt.Client.Benchmarks.DecodePublishBenchmark-report-github

**Codec micro-benchmark.** The inverse of the encode test: parses the bytes of one pre-encoded MQTT 5 PUBLISH back into a packet object, once per `PayloadSize`. The same wire bytes (produced once by our encoder — the on-wire format is identical) feed both decoders, so it measures pure parse cost: the var-int remaining-length, the topic, and the payload slice/allocation. No broker or socket is involved.

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]   : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method      | PayloadSize | Mean        | Error       | StdDev     | Ratio | RatioSD | Gen0   | Gen1   | Gen2   | Allocated | Alloc Ratio |
|------------ |------------ |------------:|------------:|-----------:|------:|--------:|-------:|-------:|-------:|----------:|------------:|
| **MQTTnet**     | **64**          |    **89.47 ns** |    **66.93 ns** |   **3.669 ns** |  **1.00** |    **0.05** | **0.0082** |      **-** |      **-** |     **248 B** |        **1.00** |
| Mqtt.Client | 64          |    97.97 ns |    11.53 ns |   0.632 ns |  1.10 |    0.04 | 0.0098 |      - |      - |     200 B |        0.81 |
|             |             |             |             |            |       |         |        |        |        |           |             |
| **MQTTnet**     | **256**         |   **120.97 ns** |   **162.10 ns** |   **8.886 ns** |  **1.00** |    **0.09** | **0.0145** |      **-** |      **-** |     **440 B** |        **1.00** |
| Mqtt.Client | 256         |   137.66 ns |   191.55 ns |  10.499 ns |  1.14 |    0.10 | 0.0129 |      - |      - |     392 B |        0.89 |
|             |             |             |             |            |       |         |        |        |        |           |             |
| **MQTTnet**     | **1024**        |   **194.95 ns** |   **197.00 ns** |  **10.798 ns** |  **1.00** |    **0.07** | **0.0403** |      **-** |      **-** |    **1208 B** |        **1.00** |
| Mqtt.Client | 1024        |   205.69 ns |    58.60 ns |   3.212 ns |  1.06 |    0.05 | 0.0386 |      - |      - |    1160 B |        0.96 |
|             |             |             |             |            |       |         |        |        |        |           |             |
| **MQTTnet**     | **4096**        |   **615.14 ns** |   **197.18 ns** |  **10.808 ns** |  **1.00** |    **0.02** | **0.1440** |      **-** |      **-** |    **4280 B** |        **1.00** |
| Mqtt.Client | 4096        |   654.34 ns |   472.92 ns |  25.922 ns |  1.06 |    0.04 | 0.1440 |      - |      - |    4232 B |        0.99 |
|             |             |             |             |            |       |         |        |        |        |           |             |
| **MQTTnet**     | **16384**       | **2,027.53 ns** |   **959.49 ns** |  **52.593 ns** |  **1.00** |    **0.03** | **0.5760** |      **-** |      **-** |   **16568 B** |        **1.00** |
| Mqtt.Client | 16384       | 1,958.48 ns |   352.12 ns |  19.301 ns |  0.97 |    0.02 | 0.7515 | 0.0191 | 0.0038 |   27534 B |        1.66 |
|             |             |             |             |            |       |         |        |        |        |           |             |
| **MQTTnet**     | **65536**       | **6,642.97 ns** | **3,530.56 ns** | **193.522 ns** |  **1.00** |    **0.04** | **2.5482** |      **-** |      **-** |   **65720 B** |        **1.00** |
| Mqtt.Client | 65536       | 6,278.13 ns | 1,469.92 ns |  80.571 ns |  0.95 |    0.03 | 2.5482 | 0.1450 |      - |   65672 B |        1.00 |

## Mqtt.Client.Benchmarks.EncodePublishBenchmark-report-github

**Codec micro-benchmark.** Serialises a single MQTT 5 PUBLISH packet (QoS 0, topic `bench/encode`) to its wire bytes, once per `PayloadSize`. Both clients reuse one array-backed buffer across iterations (no per-operation `ArrayPool` churn) and write the fixed header separately from the payload (payload bytes are never copied into the header buffer), so it isolates the raw encode-logic cost on equal footing. No broker or socket is involved.

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]   : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method      | PayloadSize | Mean     | Error     | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------ |------------ |---------:|----------:|---------:|------:|--------:|----------:|------------:|
| **MQTTnet**     | **64**          | **30.92 ns** |  **4.434 ns** | **0.243 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Mqtt.Client | 64          | 24.37 ns | 18.011 ns | 0.987 ns |  0.79 |    0.03 |         - |          NA |
|             |             |          |           |          |       |         |           |             |
| **MQTTnet**     | **256**         | **35.40 ns** |  **3.203 ns** | **0.176 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Mqtt.Client | 256         | 26.00 ns |  2.191 ns | 0.120 ns |  0.73 |    0.00 |         - |          NA |
|             |             |          |           |          |       |         |           |             |
| **MQTTnet**     | **1024**        | **33.76 ns** | **13.037 ns** | **0.715 ns** |  **1.00** |    **0.03** |         **-** |          **NA** |
| Mqtt.Client | 1024        | 25.88 ns |  4.713 ns | 0.258 ns |  0.77 |    0.02 |         - |          NA |
|             |             |          |           |          |       |         |           |             |
| **MQTTnet**     | **4096**        | **35.08 ns** |  **4.338 ns** | **0.238 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Mqtt.Client | 4096        | 26.11 ns |  5.693 ns | 0.312 ns |  0.74 |    0.01 |         - |          NA |
|             |             |          |           |          |       |         |           |             |
| **MQTTnet**     | **16384**       | **35.66 ns** |  **7.806 ns** | **0.428 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Mqtt.Client | 16384       | 26.67 ns |  2.391 ns | 0.131 ns |  0.75 |    0.01 |         - |          NA |
|             |             |          |           |          |       |         |           |             |
| **MQTTnet**     | **65536**       | **35.18 ns** |  **5.160 ns** | **0.283 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Mqtt.Client | 65536       | 25.74 ns |  6.518 ns | 0.357 ns |  0.73 |    0.01 |         - |          NA |

## Mqtt.Client.Benchmarks.EncodeSubscribeBenchmark-report-github

**Codec micro-benchmark.** Serialises one MQTT 5 SUBSCRIBE packet carrying two topic filters (`sensors/+/temp` at QoS 1 and `commands/#` at QoS 0) to its wire bytes. There is no payload-size parameter — it is a small, fixed packet that exercises topic-filter and per-filter QoS/options encoding rather than bulk payload throughput.

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]   : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method      | Mean     | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|------------ |---------:|---------:|---------:|------:|----------:|------------:|
| MQTTnet     | 56.26 ns | 6.215 ns | 0.341 ns |  1.00 |         - |          NA |
| Mqtt.Client | 49.44 ns | 8.520 ns | 0.467 ns |  0.88 |         - |          NA |

## Mqtt.Client.Benchmarks.PublishQoS0Benchmark-report-github

**End-to-end.** Times a single `PublishAsync` at QoS 0 (at-most-once — fire-and-forget, the broker sends no acknowledgement) per invocation, for each `PayloadSize`. This is the leanest publish path: serialise the PUBLISH and write it to the socket, with no return round-trip to await.

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]   : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method      | PayloadSize | Mean         | Error        | StdDev      | Ratio | RatioSD | Gen0    | Gen1    | Gen2    | Allocated  | Alloc Ratio |
|------------ |------------ |-------------:|-------------:|------------:|------:|--------:|--------:|--------:|--------:|-----------:|------------:|
| **MQTTnet**     | **64**          |    **42.609 μs** |     **5.801 μs** |   **0.3180 μs** |  **1.00** |    **0.01** |       **-** |       **-** |       **-** |    **1.64 KB** |        **1.00** |
| Mqtt.Client | 64          |     3.818 μs |    11.636 μs |   0.6378 μs |  0.09 |    0.01 |  0.0305 |       - |       - |    1.22 KB |        0.74 |
|             |             |              |              |             |       |         |         |         |         |            |             |
| **MQTTnet**     | **256**         |    **43.576 μs** |    **17.666 μs** |   **0.9683 μs** |  **1.00** |    **0.03** |  **0.0610** |       **-** |       **-** |    **2.01 KB** |        **1.00** |
| Mqtt.Client | 256         |     5.604 μs |     2.942 μs |   0.1612 μs |  0.13 |    0.00 |  0.0458 |       - |       - |    1.41 KB |        0.70 |
|             |             |              |              |             |       |         |         |         |         |            |             |
| **MQTTnet**     | **1024**        |    **47.177 μs** |   **101.591 μs** |   **5.5685 μs** |  **1.01** |    **0.14** |  **0.0610** |       **-** |       **-** |     **3.5 KB** |        **1.00** |
| Mqtt.Client | 1024        |     7.810 μs |     7.314 μs |   0.4009 μs |  0.17 |    0.02 |  0.1373 |  0.0305 |  0.0305 |    2.93 KB |        0.84 |
|             |             |              |              |             |       |         |         |         |         |            |             |
| **MQTTnet**     | **4096**        |    **43.250 μs** |     **6.171 μs** |   **0.3383 μs** |  **1.00** |    **0.01** |  **0.3052** |       **-** |       **-** |    **9.48 KB** |        **1.00** |
| Mqtt.Client | 4096        |    15.278 μs |     5.173 μs |   0.2836 μs |  0.35 |    0.01 |  0.3052 |       - |       - |    9.04 KB |        0.95 |
|             |             |              |              |             |       |         |         |         |         |            |             |
| **MQTTnet**     | **16384**       |    **41.953 μs** |     **6.346 μs** |   **0.3479 μs** |  **1.00** |    **0.01** |  **1.1597** |  **0.0610** |       **-** |    **33.5 KB** |        **1.00** |
| Mqtt.Client | 16384       |    49.871 μs |    54.801 μs |   3.0038 μs |  1.19 |    0.06 |  1.0986 |  0.0610 |       - |   33.15 KB |        0.99 |
|             |             |              |              |             |       |         |         |         |         |            |             |
| **MQTTnet**     | **65536**       |   **182.455 μs** |   **495.766 μs** |  **27.1746 μs** |  **1.01** |    **0.19** |  **5.8594** |  **0.7324** |       **-** |  **130.23 KB** |        **1.00** |
| Mqtt.Client | 65536       |   179.449 μs |    21.585 μs |   1.1832 μs |  1.00 |    0.13 |  4.3945 |  0.9766 |       - |  130.22 KB |        1.00 |
|             |             |              |              |             |       |         |         |         |         |            |             |
| **MQTTnet**     | **1048576**     | **3,038.734 μs** | **5,987.353 μs** | **328.1870 μs** |  **1.01** |    **0.13** | **70.3125** | **70.3125** | **70.3125** | **2056.11 KB** |        **1.00** |
| Mqtt.Client | 1048576     | 2,830.418 μs | 2,454.551 μs | 134.5422 μs |  0.94 |    0.09 | 74.2188 | 74.2188 | 74.2188 | 2065.94 KB |        1.00 |

## Mqtt.Client.Benchmarks.PublishQoS1Benchmark-report-github

**End-to-end.** Times a single `PublishAsync` at QoS 1 (at-least-once) per invocation, for each `PayloadSize`. The call completes only once the broker's PUBACK arrives, so each measurement includes one network round-trip plus the packet-id allocation and ack-correlation work.

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]   : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method      | PayloadSize | Mean       | Error       | StdDev    | Ratio | RatioSD | Gen0    | Gen1    | Gen2    | Allocated  | Alloc Ratio |
|------------ |------------ |-----------:|------------:|----------:|------:|--------:|--------:|--------:|--------:|-----------:|------------:|
| **MQTTnet**     | **64**          |   **150.7 μs** |    **15.41 μs** |   **0.84 μs** |  **1.00** |    **0.01** |       **-** |       **-** |       **-** |    **3.81 KB** |        **1.00** |
| Mqtt.Client | 64          |   119.3 μs |    27.39 μs |   1.50 μs |  0.79 |    0.01 |       - |       - |       - |     2.5 KB |        0.66 |
|             |             |            |             |           |       |         |         |         |         |            |             |
| **MQTTnet**     | **256**         |   **152.2 μs** |    **41.71 μs** |   **2.29 μs** |  **1.00** |    **0.02** |       **-** |       **-** |       **-** |    **4.17 KB** |        **1.00** |
| Mqtt.Client | 256         |   117.2 μs |    14.59 μs |   0.80 μs |  0.77 |    0.01 |       - |       - |       - |    2.88 KB |        0.69 |
|             |             |            |             |           |       |         |         |         |         |            |             |
| **MQTTnet**     | **1024**        |   **142.9 μs** |   **117.62 μs** |   **6.45 μs** |  **1.00** |    **0.05** |       **-** |       **-** |       **-** |    **5.67 KB** |        **1.00** |
| Mqtt.Client | 1024        |   115.1 μs |    34.43 μs |   1.89 μs |  0.81 |    0.03 |       - |       - |       - |    4.38 KB |        0.77 |
|             |             |            |             |           |       |         |         |         |         |            |             |
| **MQTTnet**     | **4096**        |   **148.8 μs** |    **96.45 μs** |   **5.29 μs** |  **1.00** |    **0.04** |  **0.2441** |       **-** |       **-** |   **11.73 KB** |        **1.00** |
| Mqtt.Client | 4096        |   125.7 μs |   151.18 μs |   8.29 μs |  0.85 |    0.06 |  0.2441 |       - |       - |   10.45 KB |        0.89 |
|             |             |            |             |           |       |         |         |         |         |            |             |
| **MQTTnet**     | **16384**       |   **164.4 μs** |    **87.47 μs** |   **4.79 μs** |  **1.00** |    **0.04** |  **1.2207** |       **-** |       **-** |   **35.94 KB** |        **1.00** |
| Mqtt.Client | 16384       |   152.2 μs |   139.10 μs |   7.62 μs |  0.93 |    0.05 |  1.2207 |       - |       - |   34.66 KB |        0.96 |
|             |             |            |             |           |       |         |         |         |         |            |             |
| **MQTTnet**     | **65536**       |   **245.6 μs** |   **276.63 μs** |  **15.16 μs** |  **1.00** |    **0.07** |  **4.8828** |  **0.4883** |       **-** |  **132.82 KB** |        **1.00** |
| Mqtt.Client | 65536       |   235.9 μs |   252.70 μs |  13.85 μs |  0.96 |    0.07 |  4.3945 |  0.4883 |       - |  131.53 KB |        0.99 |
|             |             |            |             |           |       |         |         |         |         |            |             |
| **MQTTnet**     | **1048576**     | **3,335.7 μs** | **3,666.54 μs** | **200.98 μs** |  **1.00** |    **0.07** | **62.5000** | **62.5000** | **62.5000** | **2071.13 KB** |        **1.00** |
| Mqtt.Client | 1048576     | 3,244.4 μs | 4,455.32 μs | 244.21 μs |  0.97 |    0.08 | 66.4063 | 66.4063 | 66.4063 | 2069.22 KB |        1.00 |

## Mqtt.Client.Benchmarks.PublishQoS2Benchmark-report-github

**End-to-end.** Times a single `PublishAsync` at QoS 2 (exactly-once) per invocation, for each `PayloadSize`. This drives the full four-packet handshake — PUBLISH → PUBREC → PUBREL → PUBCOMP, i.e. two round-trips — making it the most expensive delivery guarantee to benchmark.

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]   : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method      | PayloadSize | Mean       | Error       | StdDev    | Ratio | RatioSD | Gen0    | Gen1    | Gen2    | Allocated | Alloc Ratio |
|------------ |------------ |-----------:|------------:|----------:|------:|--------:|--------:|--------:|--------:|----------:|------------:|
| **MQTTnet**     | **64**          |   **206.7 μs** |   **112.86 μs** |   **6.19 μs** |  **1.00** |    **0.04** |       **-** |       **-** |       **-** |    **6793 B** |        **1.00** |
| Mqtt.Client | 64          |   221.5 μs |   560.19 μs |  30.71 μs |  1.07 |    0.13 |       - |       - |       - |    4112 B |        0.61 |
|             |             |            |             |           |       |         |         |         |         |           |             |
| **MQTTnet**     | **256**         |   **286.7 μs** |    **68.43 μs** |   **3.75 μs** |  **1.00** |    **0.02** |       **-** |       **-** |       **-** |    **7251 B** |        **1.00** |
| Mqtt.Client | 256         |   249.4 μs |   308.00 μs |  16.88 μs |  0.87 |    0.05 |       - |       - |       - |    4496 B |        0.62 |
|             |             |            |             |           |       |         |         |         |         |           |             |
| **MQTTnet**     | **1024**        |   **236.3 μs** |    **76.58 μs** |   **4.20 μs** |  **1.00** |    **0.02** |       **-** |       **-** |       **-** |    **8699 B** |        **1.00** |
| Mqtt.Client | 1024        |   252.1 μs |    92.89 μs |   5.09 μs |  1.07 |    0.02 |       - |       - |       - |    6032 B |        0.69 |
|             |             |            |             |           |       |         |         |         |         |           |             |
| **MQTTnet**     | **4096**        |   **271.6 μs** |   **462.28 μs** |  **25.34 μs** |  **1.01** |    **0.11** |  **0.4883** |       **-** |       **-** |   **14948 B** |        **1.00** |
| Mqtt.Client | 4096        |   228.7 μs |   330.22 μs |  18.10 μs |  0.85 |    0.09 |       - |       - |       - |   12248 B |        0.82 |
|             |             |            |             |           |       |         |         |         |         |           |             |
| **MQTTnet**     | **16384**       |   **258.7 μs** |   **103.48 μs** |   **5.67 μs** |  **1.00** |    **0.03** |  **0.9766** |       **-** |       **-** |   **39706 B** |        **1.00** |
| Mqtt.Client | 16384       |   222.7 μs |   145.01 μs |   7.95 μs |  0.86 |    0.03 |  1.9531 |  0.4883 |  0.4883 |         - |        0.00 |
|             |             |            |             |           |       |         |         |         |         |           |             |
| **MQTTnet**     | **65536**       |   **289.0 μs** |    **74.90 μs** |   **4.11 μs** |  **1.00** |    **0.02** |  **4.3945** |  **0.4883** |       **-** |  **138883 B** |        **1.00** |
| Mqtt.Client | 65536       |   267.0 μs |   158.13 μs |   8.67 μs |  0.92 |    0.03 |  4.8828 |  0.4883 |       - |  136208 B |        0.98 |
|             |             |            |             |           |       |         |         |         |         |           |             |
| **MQTTnet**     | **1048576**     | **2,987.7 μs** | **4,734.83 μs** | **259.53 μs** |  **1.01** |    **0.11** | **66.4063** | **66.4063** | **66.4063** | **2123373 B** |        **1.00** |
| Mqtt.Client | 1048576     | 2,890.5 μs | 8,079.00 μs | 442.84 μs |  0.97 |    0.15 | 66.4063 | 66.4063 | 66.4063 | 2120338 B |        1.00 |

## Mqtt.Client.Benchmarks.SubscribeReceiveBenchmark-report-github

**End-to-end.** Measures receive throughput: a separate publisher connection publishes one QoS 0 message and the subscribed client-under-test waits to read it from its channel, for each `PayloadSize`. The application messages are pre-built during setup, so only publish → broker fan-out → client receive/dispatch is timed.

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]   : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                | PayloadSize | Mean       | Error       | StdDev    | Ratio | RatioSD | Gen0     | Gen1     | Gen2     | Allocated  | Alloc Ratio |
|---------------------- |------------ |-----------:|------------:|----------:|------:|--------:|---------:|---------:|---------:|-----------:|------------:|
| **&#39;MQTTnet receive&#39;**     | **64**          |   **126.0 μs** |    **72.63 μs** |   **3.98 μs** |  **1.00** |    **0.04** |        **-** |        **-** |        **-** |     **4.7 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 64          |   126.9 μs |    64.61 μs |   3.54 μs |  1.01 |    0.04 |        - |        - |        - |    3.31 KB |        0.70 |
|                       |             |            |             |           |       |         |          |          |          |            |             |
| **&#39;MQTTnet receive&#39;**     | **256**         |   **139.7 μs** |    **12.15 μs** |   **0.67 μs** |  **1.00** |    **0.01** |        **-** |        **-** |        **-** |    **5.44 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 256         |   161.2 μs |   316.43 μs |  17.34 μs |  1.15 |    0.11 |        - |        - |        - |    3.96 KB |        0.73 |
|                       |             |            |             |           |       |         |          |          |          |            |             |
| **&#39;MQTTnet receive&#39;**     | **1024**        |   **184.0 μs** |   **272.06 μs** |  **14.91 μs** |  **1.00** |    **0.10** |   **0.2441** |        **-** |        **-** |     **8.6 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 1024        |   154.8 μs |   316.69 μs |  17.36 μs |  0.84 |    0.10 |        - |        - |        - |    6.13 KB |        0.71 |
|                       |             |            |             |           |       |         |          |          |          |            |             |
| **&#39;MQTTnet receive&#39;**     | **4096**        |   **150.9 μs** |   **127.60 μs** |   **6.99 μs** |  **1.00** |    **0.06** |   **0.4883** |        **-** |        **-** |   **20.57 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 4096        |   208.7 μs |   178.16 μs |   9.77 μs |  1.38 |    0.08 |   0.4883 |        - |        - |   15.24 KB |        0.74 |
|                       |             |            |             |           |       |         |          |          |          |            |             |
| **&#39;MQTTnet receive&#39;**     | **16384**       |   **346.4 μs** |   **228.75 μs** |  **12.54 μs** |  **1.00** |    **0.04** |   **2.4414** |        **-** |        **-** |   **69.09 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 16384       |   186.6 μs |   270.23 μs |  14.81 μs |  0.54 |    0.04 |   1.4648 |        - |        - |   51.41 KB |        0.74 |
|                       |             |            |             |           |       |         |          |          |          |            |             |
| **&#39;MQTTnet receive&#39;**     | **65536**       |   **469.4 μs** | **1,081.65 μs** |  **59.29 μs** |  **1.01** |    **0.16** |  **10.2539** |   **3.4180** |   **0.9766** |  **262.95 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 65536       |   389.4 μs |   105.39 μs |   5.78 μs |  0.84 |    0.10 |   8.3008 |   2.4414 |   0.9766 |  196.34 KB |        0.75 |
|                       |             |            |             |           |       |         |          |          |          |            |             |
| **&#39;MQTTnet receive&#39;**     | **1048576**     | **7,261.6 μs** | **7,855.83 μs** | **430.60 μs** |  **1.00** |    **0.07** | **125.0000** | **125.0000** | **125.0000** | **4139.07 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 1048576     | 4,612.4 μs | 9,781.05 μs | 536.13 μs |  0.64 |    0.07 |  93.7500 |  93.7500 |  93.7500 | 3094.68 KB |        0.75 |

