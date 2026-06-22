# Mqtt.Client vs MQTTnet — benchmark results

_Generated 2026-06-21 19:04 UTC._

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
  [Host]     : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  Job-SMYQSW : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

InvocationCount=16  IterationCount=10  UnrollFactor=1  
WarmupCount=3  

```
| Method      | Mean     | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------ |---------:|----------:|----------:|------:|--------:|----------:|------------:|
| MQTTnet     | 4.210 ms | 0.5192 ms | 0.3434 ms |  1.01 |    0.11 |  38.94 KB |        1.00 |
| Mqtt.Client | 3.936 ms | 0.4784 ms | 0.2847 ms |  0.94 |    0.09 |  43.57 KB |        1.12 |

## Mqtt.Client.Benchmarks.DecodePublishBenchmark-report-github

**Codec micro-benchmark.** The inverse of the encode test: parses the bytes of one pre-encoded MQTT 5 PUBLISH back into a packet object, once per `PayloadSize`. The same wire bytes (produced once by our encoder — the on-wire format is identical) feed both decoders, so it measures pure parse cost: the var-int remaining-length, the topic, and the payload slice/allocation. No broker or socket is involved.

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  DefaultJob : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL


```
| Method      | PayloadSize | Mean        | Error      | StdDev     | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------ |------------ |------------:|-----------:|-----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **MQTTnet**     | **64**          |    **86.11 ns** |   **1.756 ns** |   **3.507 ns** |  **1.00** |    **0.06** | **0.0082** |      **-** |     **248 B** |        **1.00** |
| Mqtt.Client | 64          |   101.80 ns |   2.071 ns |   4.039 ns |  1.18 |    0.07 | 0.0066 |      - |     200 B |        0.81 |
|             |             |             |            |            |       |         |        |        |           |             |
| **MQTTnet**     | **256**         |   **120.82 ns** |   **2.472 ns** |   **5.159 ns** |  **1.00** |    **0.06** | **0.0145** |      **-** |     **440 B** |        **1.00** |
| Mqtt.Client | 256         |   136.81 ns |   2.798 ns |   4.437 ns |  1.13 |    0.06 | 0.0129 |      - |     392 B |        0.89 |
|             |             |             |            |            |       |         |        |        |           |             |
| **MQTTnet**     | **1024**        |   **217.68 ns** |   **4.413 ns** |   **7.374 ns** |  **1.00** |    **0.05** | **0.0403** |      **-** |    **1208 B** |        **1.00** |
| Mqtt.Client | 1024        |   299.57 ns |  14.275 ns |  42.090 ns |  1.38 |    0.20 | 0.0386 |      - |    1160 B |        0.96 |
|             |             |             |            |            |       |         |        |        |           |             |
| **MQTTnet**     | **4096**        |   **833.65 ns** |  **22.792 ns** |  **67.204 ns** |  **1.01** |    **0.11** | **0.1440** |      **-** |    **4280 B** |        **1.00** |
| Mqtt.Client | 4096        |   885.95 ns |  17.735 ns |  48.248 ns |  1.07 |    0.10 | 0.1431 |      - |    4232 B |        0.99 |
|             |             |             |            |            |       |         |        |        |           |             |
| **MQTTnet**     | **16384**       | **2,407.72 ns** |  **47.729 ns** | **119.744 ns** |  **1.00** |    **0.07** | **0.5760** |      **-** |   **16568 B** |        **1.00** |
| Mqtt.Client | 16384       | 2,505.00 ns |  60.878 ns | 172.702 ns |  1.04 |    0.09 | 0.5760 | 0.0114 |   16520 B |        1.00 |
|             |             |             |            |            |       |         |        |        |           |             |
| **MQTTnet**     | **65536**       | **7,253.32 ns** | **157.945 ns** | **465.705 ns** |  **1.00** |    **0.09** | **2.5482** |      **-** |   **65720 B** |        **1.00** |
| Mqtt.Client | 65536       | 7,217.30 ns | 144.304 ns | 313.705 ns |  1.00 |    0.08 | 2.5482 | 0.1450 |   65672 B |        1.00 |

## Mqtt.Client.Benchmarks.EncodePublishBenchmark-report-github

**Codec micro-benchmark.** Serialises a single MQTT 5 PUBLISH packet (QoS 0, topic `bench/encode`) to its wire bytes, once per `PayloadSize`. Both clients reuse one array-backed buffer across iterations (no per-operation `ArrayPool` churn) and write the fixed header separately from the payload (payload bytes are never copied into the header buffer), so it isolates the raw encode-logic cost on equal footing. No broker or socket is involved.

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  DefaultJob : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL


```
| Method      | PayloadSize | Mean     | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------ |------------ |---------:|---------:|---------:|------:|--------:|----------:|------------:|
| **MQTTnet**     | **64**          | **31.92 ns** | **0.648 ns** | **0.820 ns** |  **1.00** |    **0.04** |         **-** |          **NA** |
| Mqtt.Client | 64          | 22.87 ns | 0.359 ns | 0.300 ns |  0.72 |    0.02 |         - |          NA |
|             |             |          |          |          |       |         |           |             |
| **MQTTnet**     | **256**         | **33.10 ns** | **0.380 ns** | **0.318 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Mqtt.Client | 256         | 24.78 ns | 0.513 ns | 0.829 ns |  0.75 |    0.03 |         - |          NA |
|             |             |          |          |          |       |         |           |             |
| **MQTTnet**     | **1024**        | **32.60 ns** | **0.576 ns** | **0.539 ns** |  **1.00** |    **0.02** |         **-** |          **NA** |
| Mqtt.Client | 1024        | 24.42 ns | 0.507 ns | 0.622 ns |  0.75 |    0.02 |         - |          NA |
|             |             |          |          |          |       |         |           |             |
| **MQTTnet**     | **4096**        | **35.91 ns** | **0.542 ns** | **0.507 ns** |  **1.00** |    **0.02** |         **-** |          **NA** |
| Mqtt.Client | 4096        | 24.23 ns | 0.344 ns | 0.287 ns |  0.68 |    0.01 |         - |          NA |
|             |             |          |          |          |       |         |           |             |
| **MQTTnet**     | **16384**       | **33.93 ns** | **0.283 ns** | **0.237 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Mqtt.Client | 16384       | 25.95 ns | 0.397 ns | 0.371 ns |  0.76 |    0.01 |         - |          NA |
|             |             |          |          |          |       |         |           |             |
| **MQTTnet**     | **65536**       | **35.14 ns** | **0.454 ns** | **0.403 ns** |  **1.00** |    **0.02** |         **-** |          **NA** |
| Mqtt.Client | 65536       | 24.20 ns | 0.104 ns | 0.097 ns |  0.69 |    0.01 |         - |          NA |

## Mqtt.Client.Benchmarks.EncodeSubscribeBenchmark-report-github

**Codec micro-benchmark.** Serialises one MQTT 5 SUBSCRIBE packet carrying two topic filters (`sensors/+/temp` at QoS 1 and `commands/#` at QoS 0) to its wire bytes. There is no payload-size parameter — it is a small, fixed packet that exercises topic-filter and per-filter QoS/options encoding rather than bulk payload throughput.

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  DefaultJob : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL


```
| Method      | Mean     | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|------------ |---------:|---------:|---------:|------:|----------:|------------:|
| MQTTnet     | 56.53 ns | 0.707 ns | 0.590 ns |  1.00 |         - |          NA |
| Mqtt.Client | 47.00 ns | 0.449 ns | 0.398 ns |  0.83 |         - |          NA |

## Mqtt.Client.Benchmarks.PublishQoS0Benchmark-report-github

**End-to-end.** Times a single `PublishAsync` at QoS 0 (at-most-once — fire-and-forget, the broker sends no acknowledgement) per invocation, for each `PayloadSize`. This is the leanest publish path: serialise the PUBLISH and write it to the socket, with no return round-trip to await.

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  DefaultJob : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL


```
| Method      | PayloadSize | Mean         | Error       | StdDev      | Median       | Ratio | RatioSD | Gen0    | Gen1    | Gen2    | Allocated  | Alloc Ratio |
|------------ |------------ |-------------:|------------:|------------:|-------------:|------:|--------:|--------:|--------:|--------:|-----------:|------------:|
| **MQTTnet**     | **64**          |    **42.241 μs** |   **0.8215 μs** |   **0.7283 μs** |    **42.326 μs** |  **1.00** |    **0.02** |       **-** |       **-** |       **-** |    **1.64 KB** |        **1.00** |
| Mqtt.Client | 64          |     3.691 μs |   0.0716 μs |   0.0955 μs |     3.687 μs |  0.09 |    0.00 |  0.0305 |       - |       - |    1.06 KB |        0.65 |
|             |             |              |             |             |              |       |         |         |         |         |            |             |
| **MQTTnet**     | **256**         |    **42.293 μs** |   **0.8409 μs** |   **1.2060 μs** |    **42.011 μs** |  **1.00** |    **0.04** |  **0.0610** |       **-** |       **-** |    **2.01 KB** |        **1.00** |
| Mqtt.Client | 256         |     5.395 μs |   0.1215 μs |   0.3583 μs |     5.383 μs |  0.13 |    0.01 |  0.0458 |       - |       - |    1.44 KB |        0.72 |
|             |             |              |             |             |              |       |         |         |         |         |            |             |
| **MQTTnet**     | **1024**        |    **42.269 μs** |   **0.8310 μs** |   **1.2180 μs** |    **41.959 μs** |  **1.00** |    **0.04** |  **0.0610** |       **-** |       **-** |    **3.51 KB** |        **1.00** |
| Mqtt.Client | 1024        |     6.205 μs |   0.1240 μs |   0.3200 μs |     6.160 μs |  0.15 |    0.01 |  0.0916 |       - |       - |    3.04 KB |        0.87 |
|             |             |              |             |             |              |       |         |         |         |         |            |             |
| **MQTTnet**     | **4096**        |    **43.987 μs** |   **0.8712 μs** |   **1.4556 μs** |    **43.566 μs** |  **1.00** |    **0.05** |  **0.3052** |       **-** |       **-** |    **9.49 KB** |        **1.00** |
| Mqtt.Client | 4096        |    11.124 μs |   0.2952 μs |   0.8565 μs |    11.063 μs |  0.25 |    0.02 |  0.3052 |       - |       - |       9 KB |        0.95 |
|             |             |              |             |             |              |       |         |         |         |         |            |             |
| **MQTTnet**     | **16384**       |    **32.234 μs** |   **0.6289 μs** |   **0.9019 μs** |    **32.083 μs** |  **1.00** |    **0.04** |  **1.1597** |       **-** |       **-** |   **33.41 KB** |        **1.00** |
| Mqtt.Client | 16384       |    33.739 μs |   0.6702 μs |   1.5927 μs |    33.794 μs |  1.05 |    0.06 |  1.1597 |  0.0610 |       - |   33.55 KB |        1.00 |
|             |             |              |             |             |              |       |         |         |         |         |            |             |
| **MQTTnet**     | **65536**       |   **225.947 μs** |   **7.1632 μs** |  **20.8953 μs** |   **224.825 μs** |  **1.01** |    **0.13** |  **7.3242** |  **1.9531** |  **0.4883** | **1350.26 KB** |        **1.00** |
| Mqtt.Client | 65536       |   142.886 μs |   2.8252 μs |   3.0229 μs |   142.795 μs |  0.64 |    0.06 |  4.6387 |  0.9766 |       - |  129.93 KB |        0.10 |
|             |             |              |             |             |              |       |         |         |         |         |            |             |
| **MQTTnet**     | **1048576**     | **2,892.790 μs** |  **67.4721 μs** | **195.7489 μs** | **2,882.621 μs** |  **1.00** |    **0.10** | **74.2188** | **74.2188** | **74.2188** | **2066.58 KB** |        **1.00** |
| Mqtt.Client | 1048576     | 3,806.390 μs | 142.7988 μs | 421.0455 μs | 3,683.150 μs |  1.32 |    0.17 | 67.3828 | 67.3828 | 67.3828 | 2075.26 KB |        1.00 |

## Mqtt.Client.Benchmarks.PublishQoS1Benchmark-report-github

**End-to-end.** Times a single `PublishAsync` at QoS 1 (at-least-once) per invocation, for each `PayloadSize`. The call completes only once the broker's PUBACK arrives, so each measurement includes one network round-trip plus the packet-id allocation and ack-correlation work.

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  DefaultJob : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL


```
| Method      | PayloadSize | Mean        | Error     | StdDev     | Median      | Ratio | RatioSD | Gen0    | Gen1    | Gen2    | Allocated  | Alloc Ratio |
|------------ |------------ |------------:|----------:|-----------:|------------:|------:|--------:|--------:|--------:|--------:|-----------:|------------:|
| **MQTTnet**     | **64**          |   **118.50 μs** |  **3.180 μs** |   **9.377 μs** |   **117.34 μs** |  **1.01** |    **0.11** |       **-** |       **-** |       **-** |    **3.78 KB** |        **1.00** |
| Mqtt.Client | 64          |    90.92 μs |  1.784 μs |   3.394 μs |    91.04 μs |  0.77 |    0.07 |       - |       - |       - |    2.47 KB |        0.65 |
|             |             |             |           |            |             |       |         |         |         |         |            |             |
| **MQTTnet**     | **256**         |   **122.61 μs** |  **3.171 μs** |   **9.351 μs** |   **124.59 μs** |  **1.01** |    **0.11** |       **-** |       **-** |       **-** |    **4.16 KB** |        **1.00** |
| Mqtt.Client | 256         |   103.04 μs |  1.964 μs |   4.513 μs |   102.39 μs |  0.85 |    0.08 |       - |       - |       - |    2.84 KB |        0.68 |
|             |             |             |           |            |             |       |         |         |         |         |            |             |
| **MQTTnet**     | **1024**        |   **129.39 μs** |  **2.561 μs** |   **6.518 μs** |   **128.11 μs** |  **1.00** |    **0.07** |       **-** |       **-** |       **-** |    **5.66 KB** |        **1.00** |
| Mqtt.Client | 1024        |   116.11 μs |  3.719 μs |  10.966 μs |   115.09 μs |  0.90 |    0.10 |       - |       - |       - |    4.34 KB |        0.77 |
|             |             |             |           |            |             |       |         |         |         |         |            |             |
| **MQTTnet**     | **4096**        |   **139.95 μs** |  **2.794 μs** |   **7.505 μs** |   **139.13 μs** |  **1.00** |    **0.07** |  **0.2441** |       **-** |       **-** |   **11.72 KB** |        **1.00** |
| Mqtt.Client | 4096        |   109.47 μs |  2.167 μs |   5.969 μs |   108.84 μs |  0.78 |    0.06 |  0.2441 |       - |       - |   10.41 KB |        0.89 |
|             |             |             |           |            |             |       |         |         |         |         |            |             |
| **MQTTnet**     | **16384**       |   **138.27 μs** |  **2.752 μs** |   **6.541 μs** |   **138.67 μs** |  **1.00** |    **0.07** |  **1.2207** |       **-** |       **-** |   **35.93 KB** |        **1.00** |
| Mqtt.Client | 16384       |   155.68 μs |  4.510 μs |  13.226 μs |   151.56 μs |  1.13 |    0.11 |  1.2207 |       - |       - |   34.63 KB |        0.96 |
|             |             |             |           |            |             |       |         |         |         |         |            |             |
| **MQTTnet**     | **65536**       |   **174.28 μs** |  **3.420 μs** |   **6.339 μs** |   **173.29 μs** |  **1.00** |    **0.05** |  **4.8828** |  **0.7324** |       **-** |  **132.78 KB** |        **1.00** |
| Mqtt.Client | 65536       |   265.03 μs |  5.279 μs |  13.244 μs |   259.70 μs |  1.52 |    0.09 |  4.8828 |  0.4883 |       - |  131.64 KB |        0.99 |
|             |             |             |           |            |             |       |         |         |         |         |            |             |
| **MQTTnet**     | **1048576**     | **2,899.70 μs** | **68.881 μs** | **203.097 μs** | **2,900.74 μs** |  **1.00** |    **0.10** | **66.4063** | **66.4063** | **66.4063** | **2070.91 KB** |        **1.00** |
| Mqtt.Client | 1048576     | 4,562.71 μs | 90.433 μs | 211.384 μs | 4,572.38 μs |  1.58 |    0.13 | 62.5000 | 62.5000 | 62.5000 | 2075.15 KB |        1.00 |

## Mqtt.Client.Benchmarks.PublishQoS2Benchmark-report-github

**End-to-end.** Times a single `PublishAsync` at QoS 2 (exactly-once) per invocation, for each `PayloadSize`. This drives the full four-packet handshake — PUBLISH → PUBREC → PUBREL → PUBCOMP, i.e. two round-trips — making it the most expensive delivery guarantee to benchmark.

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  DefaultJob : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL


```
| Method      | PayloadSize | Mean       | Error    | StdDev    | Median     | Ratio | RatioSD | Gen0    | Gen1    | Gen2    | Allocated  | Alloc Ratio |
|------------ |------------ |-----------:|---------:|----------:|-----------:|------:|--------:|--------:|--------:|--------:|-----------:|------------:|
| **MQTTnet**     | **64**          |   **228.9 μs** |  **7.14 μs** |  **20.93 μs** |   **225.6 μs** |  **1.01** |    **0.13** |       **-** |       **-** |       **-** |    **6.65 KB** |        **1.00** |
| Mqtt.Client | 64          |   196.5 μs |  4.85 μs |  14.15 μs |   195.7 μs |  0.87 |    0.10 |       - |       - |       - |    3.95 KB |        0.59 |
|             |             |            |          |           |            |       |         |         |         |         |            |             |
| **MQTTnet**     | **256**         |   **208.9 μs** |  **8.34 μs** |  **24.34 μs** |   **201.0 μs** |  **1.01** |    **0.16** |       **-** |       **-** |       **-** |    **6.99 KB** |        **1.00** |
| Mqtt.Client | 256         |   178.1 μs |  4.57 μs |  13.27 μs |   176.4 μs |  0.86 |    0.11 |       - |       - |       - |    4.33 KB |        0.62 |
|             |             |            |          |           |            |       |         |         |         |         |            |             |
| **MQTTnet**     | **1024**        |   **182.5 μs** |  **3.57 μs** |   **6.44 μs** |   **181.0 μs** |  **1.00** |    **0.05** |       **-** |       **-** |       **-** |    **8.49 KB** |        **1.00** |
| Mqtt.Client | 1024        |   163.0 μs |  3.15 μs |   7.68 μs |   162.2 μs |  0.89 |    0.05 |       - |       - |       - |    5.83 KB |        0.69 |
|             |             |            |          |           |            |       |         |         |         |         |            |             |
| **MQTTnet**     | **4096**        |   **201.5 μs** |  **5.21 μs** |  **14.53 μs** |   **198.2 μs** |  **1.00** |    **0.10** |  **0.4883** |       **-** |       **-** |   **14.57 KB** |        **1.00** |
| Mqtt.Client | 4096        |   188.8 μs |  5.25 μs |  15.24 μs |   184.7 μs |  0.94 |    0.10 |       - |       - |       - |    11.9 KB |        0.82 |
|             |             |            |          |           |            |       |         |         |         |         |            |             |
| **MQTTnet**     | **16384**       |   **226.6 μs** |  **8.21 μs** |  **24.20 μs** |   **218.2 μs** |  **1.01** |    **0.15** |  **0.9766** |       **-** |       **-** |   **39.06 KB** |        **1.00** |
| Mqtt.Client | 16384       |   253.5 μs |  5.96 μs |  17.00 μs |   249.2 μs |  1.13 |    0.14 |  0.9766 |       - |       - |   36.13 KB |        0.93 |
|             |             |            |          |           |            |       |         |         |         |         |            |             |
| **MQTTnet**     | **65536**       |   **292.7 μs** |  **5.85 μs** |  **12.59 μs** |   **289.4 μs** |  **1.00** |    **0.06** |  **4.8828** |  **0.4883** |       **-** |  **135.65 KB** |        **1.00** |
| Mqtt.Client | 65536       |   413.1 μs |  8.17 μs |  12.72 μs |   411.0 μs |  1.41 |    0.07 |  4.8828 |       - |       - |  133.25 KB |        0.98 |
|             |             |            |          |           |            |       |         |         |         |         |            |             |
| **MQTTnet**     | **1048576**     | **2,981.1 μs** | **72.33 μs** | **210.98 μs** | **2,964.0 μs** |  **1.00** |    **0.10** | **66.4063** | **66.4063** | **66.4063** | **2073.43 KB** |        **1.00** |
| Mqtt.Client | 1048576     | 3,802.7 μs | 87.90 μs | 246.49 μs | 3,790.2 μs |  1.28 |    0.12 | 62.5000 | 62.5000 | 62.5000 |  2074.2 KB |        1.00 |

## Mqtt.Client.Benchmarks.SubscribeReceiveBenchmark-report-github

**End-to-end.** Measures receive throughput: a separate publisher connection publishes one QoS 0 message and the subscribed client-under-test waits to read it from its channel, for each `PayloadSize`. The application messages are pre-built during setup, so only publish → broker fan-out → client receive/dispatch is timed.

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  DefaultJob : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL


```
| Method                | PayloadSize | Mean       | Error     | StdDev    | Ratio | RatioSD | Gen0     | Gen1     | Gen2     | Allocated  | Alloc Ratio |
|---------------------- |------------ |-----------:|----------:|----------:|------:|--------:|---------:|---------:|---------:|-----------:|------------:|
| **&#39;MQTTnet receive&#39;**     | **64**          |   **119.1 μs** |   **2.35 μs** |   **3.59 μs** |  **1.00** |    **0.04** |        **-** |        **-** |        **-** |     **4.7 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 64          |   120.5 μs |   2.33 μs |   3.26 μs |  1.01 |    0.04 |        - |        - |        - |    3.27 KB |        0.69 |
|                       |             |            |           |           |       |         |          |          |          |            |             |
| **&#39;MQTTnet receive&#39;**     | **256**         |   **121.4 μs** |   **2.40 μs** |   **2.76 μs** |  **1.00** |    **0.03** |        **-** |        **-** |        **-** |    **5.44 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 256         |   129.6 μs |   3.27 μs |   9.64 μs |  1.07 |    0.08 |        - |        - |        - |    3.83 KB |        0.70 |
|                       |             |            |           |           |       |         |          |          |          |            |             |
| **&#39;MQTTnet receive&#39;**     | **1024**        |   **124.1 μs** |   **2.47 μs** |   **4.12 μs** |  **1.00** |    **0.05** |   **0.2441** |        **-** |        **-** |    **8.44 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 1024        |   126.9 μs |   2.39 μs |   4.55 μs |  1.02 |    0.05 |        - |        - |        - |    6.08 KB |        0.72 |
|                       |             |            |           |           |       |         |          |          |          |            |             |
| **&#39;MQTTnet receive&#39;**     | **4096**        |   **140.4 μs** |   **2.80 μs** |   **4.44 μs** |  **1.00** |    **0.04** |   **0.4883** |        **-** |        **-** |   **20.57 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 4096        |   157.4 μs |   3.04 μs |   3.25 μs |  1.12 |    0.04 |   0.4883 |        - |        - |   15.15 KB |        0.74 |
|                       |             |            |           |           |       |         |          |          |          |            |             |
| **&#39;MQTTnet receive&#39;**     | **16384**       |   **276.2 μs** |  **10.62 μs** |  **30.65 μs** |  **1.01** |    **0.15** |   **2.4414** |        **-** |        **-** |   **69.09 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 16384       |   190.5 μs |   3.78 μs |  10.30 μs |  0.70 |    0.08 |   1.4648 |        - |        - |   51.37 KB |        0.74 |
|                       |             |            |           |           |       |         |          |          |          |            |             |
| **&#39;MQTTnet receive&#39;**     | **65536**       |   **647.5 μs** |  **24.91 μs** |  **70.25 μs** |  **1.01** |    **0.15** |   **8.7891** |   **1.9531** |        **-** |     **263 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 65536       |   268.9 μs |   4.97 μs |   4.65 μs |  0.42 |    0.05 |   8.7891 |   2.4414 |   0.9766 |  196.25 KB |        0.75 |
|                       |             |            |           |           |       |         |          |          |          |            |             |
| **&#39;MQTTnet receive&#39;**     | **1048576**     | **6,756.8 μs** | **183.99 μs** | **521.94 μs** |  **1.01** |    **0.11** | **125.0000** | **125.0000** | **125.0000** | **4138.56 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 1048576     | 4,692.7 μs | 115.44 μs | 338.58 μs |  0.70 |    0.07 |  93.7500 |  93.7500 |  93.7500 | 3094.54 KB |        0.75 |

