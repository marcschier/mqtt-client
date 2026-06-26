# Mqtt.Client vs MQTTnet — benchmark results

_Generated 2026-06-23 09:32 UTC._

[MQTTnet](https://github.com/dotnet/MQTTnet) is a mature, battle-tested .NET MQTT library. These benchmarks are not a verdict on MQTTnet — they exist to make tradeoffs visible for callers choosing between the two clients. See the README's "When to pick MQTTnet instead" section for guidance.

Each section below opens with a one-line note on what that benchmark measures. They fall into two groups: **codec micro-benchmarks** (in-memory encode/decode — no broker, no network) and **end-to-end benchmarks** (a real in-process MQTTnet broker over a TCP loopback, exercising the full client stack per operation). In every table the MQTTnet row is the baseline (Ratio = 1.00), and `PayloadSize` is the MQTT payload length in bytes.

Read each `Ratio` next to its `Error`/`StdDev` columns. The end-to-end benchmarks carry real run-to-run variance (loopback scheduling, GC, and other processes on the machine), so a single cell that is within ~10–15% of 1.00 — or that is not corroborated by the neighbouring payload sizes and the other QoS levels — is noise, not a regression. The codec micro-benchmarks and the allocation columns are the more stable signal.

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
  Job-NOACHJ : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

OutlierMode=RemoveUpper  InvocationCount=16  IterationCount=10  
MaxIterationCount=30  MinIterationCount=15  UnrollFactor=1  
WarmupCount=3  

```
| Method      | Mean     | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------ |---------:|----------:|----------:|------:|--------:|----------:|------------:|
| MQTTnet     | 3.728 ms | 0.3018 ms | 0.1578 ms |  1.00 |    0.06 |  38.58 KB |        1.00 |
| Mqtt.Client | 3.822 ms | 0.3134 ms | 0.1639 ms |  1.03 |    0.06 |  33.96 KB |        0.88 |

## Mqtt.Client.Benchmarks.DecodePublishBenchmark-report-github

**Codec micro-benchmark.** The inverse of the encode test: parses the bytes of one pre-encoded MQTT 5 PUBLISH back into a packet object, once per `PayloadSize`. The same wire bytes (produced once by our encoder — the on-wire format is identical) feed both decoders, so it measures pure parse cost: the var-int remaining-length, the topic, and the payload slice/allocation. No broker or socket is involved.

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  Job-POFKUY : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

OutlierMode=RemoveUpper  MaxIterationCount=30  MinIterationCount=15  
WarmupCount=5  

```
| Method      | PayloadSize | Mean        | Error      | StdDev     | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------ |------------ |------------:|-----------:|-----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **MQTTnet**     | **64**          |    **83.35 ns** |   **1.597 ns** |   **2.186 ns** |  **1.00** |    **0.04** | **0.0082** |      **-** |     **248 B** |        **1.00** |
| Mqtt.Client | 64          |   105.86 ns |   1.820 ns |   1.703 ns |  1.27 |    0.04 | 0.0066 |      - |     200 B |        0.81 |
|             |             |             |            |            |       |         |        |        |           |             |
| **MQTTnet**     | **256**         |   **120.31 ns** |   **1.985 ns** |   **1.549 ns** |  **1.00** |    **0.02** | **0.0145** |      **-** |     **440 B** |        **1.00** |
| Mqtt.Client | 256         |   123.92 ns |   2.470 ns |   3.297 ns |  1.03 |    0.03 | 0.0129 |      - |     392 B |        0.89 |
|             |             |             |            |            |       |         |        |        |           |             |
| **MQTTnet**     | **1024**        |   **224.14 ns** |   **3.512 ns** |   **3.286 ns** |  **1.00** |    **0.02** | **0.0401** |      **-** |    **1208 B** |        **1.00** |
| Mqtt.Client | 1024        |   220.90 ns |   4.099 ns |   3.634 ns |  0.99 |    0.02 | 0.0386 |      - |    1160 B |        0.96 |
|             |             |             |            |            |       |         |        |        |           |             |
| **MQTTnet**     | **4096**        |   **662.26 ns** |  **12.959 ns** |  **16.851 ns** |  **1.00** |    **0.03** | **0.1440** |      **-** |    **4280 B** |        **1.00** |
| Mqtt.Client | 4096        |   770.60 ns |  18.372 ns |  27.498 ns |  1.16 |    0.05 | 0.1440 |      - |    4232 B |        0.99 |
|             |             |             |            |            |       |         |        |        |           |             |
| **MQTTnet**     | **16384**       | **2,174.77 ns** |  **73.923 ns** | **110.645 ns** |  **1.00** |    **0.07** | **0.5760** |      **-** |   **16568 B** |        **1.00** |
| Mqtt.Client | 16384       | 2,244.82 ns |  72.172 ns | 105.789 ns |  1.03 |    0.07 | 0.5760 | 0.0038 |   16520 B |        1.00 |
|             |             |             |            |            |       |         |        |        |           |             |
| **MQTTnet**     | **65536**       | **7,372.81 ns** | **187.700 ns** | **275.128 ns** |  **1.00** |    **0.05** | **2.5482** |      **-** |   **65720 B** |        **1.00** |
| Mqtt.Client | 65536       | 7,409.41 ns | 138.559 ns | 108.178 ns |  1.01 |    0.04 | 2.5482 | 0.1221 |   65672 B |        1.00 |

## Mqtt.Client.Benchmarks.EncodePublishBenchmark-report-github

**Codec micro-benchmark.** Serialises a single MQTT 5 PUBLISH packet (QoS 0, topic `bench/encode`) to its wire bytes, once per `PayloadSize`. Both clients reuse one array-backed buffer across iterations (no per-operation `ArrayPool` churn) and write the fixed header separately from the payload (payload bytes are never copied into the header buffer), so it isolates the raw encode-logic cost on equal footing. No broker or socket is involved.

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  Job-POFKUY : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

OutlierMode=RemoveUpper  MaxIterationCount=30  MinIterationCount=15  
WarmupCount=5  

```
| Method      | PayloadSize | Mean     | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------ |------------ |---------:|---------:|---------:|------:|--------:|----------:|------------:|
| **MQTTnet**     | **64**          | **30.11 ns** | **0.215 ns** | **0.180 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Mqtt.Client | 64          | 23.04 ns | 0.475 ns | 0.487 ns |  0.77 |    0.02 |         - |          NA |
|             |             |          |          |          |       |         |           |             |
| **MQTTnet**     | **256**         | **33.79 ns** | **0.749 ns** | **1.074 ns** |  **1.00** |    **0.04** |         **-** |          **NA** |
| Mqtt.Client | 256         | 23.98 ns | 0.335 ns | 0.280 ns |  0.71 |    0.02 |         - |          NA |
|             |             |          |          |          |       |         |           |             |
| **MQTTnet**     | **1024**        | **33.77 ns** | **0.244 ns** | **0.228 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Mqtt.Client | 1024        | 26.58 ns | 0.531 ns | 0.590 ns |  0.79 |    0.02 |         - |          NA |
|             |             |          |          |          |       |         |           |             |
| **MQTTnet**     | **4096**        | **34.46 ns** | **0.595 ns** | **0.528 ns** |  **1.00** |    **0.02** |         **-** |          **NA** |
| Mqtt.Client | 4096        | 24.15 ns | 0.508 ns | 0.521 ns |  0.70 |    0.02 |         - |          NA |
|             |             |          |          |          |       |         |           |             |
| **MQTTnet**     | **16384**       | **35.81 ns** | **0.631 ns** | **0.560 ns** |  **1.00** |    **0.02** |         **-** |          **NA** |
| Mqtt.Client | 16384       | 27.37 ns | 0.513 ns | 0.549 ns |  0.76 |    0.02 |         - |          NA |
|             |             |          |          |          |       |         |           |             |
| **MQTTnet**     | **65536**       | **35.51 ns** | **0.755 ns** | **1.058 ns** |  **1.00** |    **0.04** |         **-** |          **NA** |
| Mqtt.Client | 65536       | 26.93 ns | 0.335 ns | 0.279 ns |  0.76 |    0.02 |         - |          NA |

## Mqtt.Client.Benchmarks.EncodeSubscribeBenchmark-report-github

**Codec micro-benchmark.** Serialises one MQTT 5 SUBSCRIBE packet carrying two topic filters (`sensors/+/temp` at QoS 1 and `commands/#` at QoS 0) to its wire bytes. There is no payload-size parameter — it is a small, fixed packet that exercises topic-filter and per-filter QoS/options encoding rather than bulk payload throughput.

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  Job-POFKUY : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

OutlierMode=RemoveUpper  MaxIterationCount=30  MinIterationCount=15  
WarmupCount=5  

```
| Method      | Mean     | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------ |---------:|---------:|---------:|------:|--------:|----------:|------------:|
| MQTTnet     | 57.89 ns | 1.193 ns | 1.634 ns |  1.00 |    0.04 |         - |          NA |
| Mqtt.Client | 52.49 ns | 1.024 ns | 1.006 ns |  0.91 |    0.03 |         - |          NA |

## Mqtt.Client.Benchmarks.PublishQoS0Benchmark-report-github

**End-to-end.** Times a single `PublishAsync` at QoS 0 (at-most-once — fire-and-forget, the broker sends no acknowledgement) per invocation, for each `PayloadSize`. This is the leanest publish path: serialise the PUBLISH and write it to the socket, with no return round-trip to await.

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  Job-POFKUY : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

OutlierMode=RemoveUpper  MaxIterationCount=30  MinIterationCount=15  
WarmupCount=5  

```
| Method      | PayloadSize | Mean         | Error       | StdDev      | Ratio | RatioSD | Gen0    | Gen1    | Gen2    | Allocated  | Alloc Ratio |
|------------ |------------ |-------------:|------------:|------------:|------:|--------:|--------:|--------:|--------:|-----------:|------------:|
| **MQTTnet**     | **64**          |    **42.284 μs** |   **0.8416 μs** |   **1.2336 μs** |  **1.00** |    **0.04** |       **-** |       **-** |       **-** |    **1.63 KB** |        **1.00** |
| Mqtt.Client | 64          |     3.950 μs |   0.1750 μs |   0.2619 μs |  0.09 |    0.01 |  0.0305 |       - |       - |    1.04 KB |        0.64 |
|             |             |              |             |             |       |         |         |         |         |            |             |
| **MQTTnet**     | **256**         |    **43.662 μs** |   **1.6267 μs** |   **2.3330 μs** |  **1.00** |    **0.07** |  **0.0610** |       **-** |       **-** |       **2 KB** |        **1.00** |
| Mqtt.Client | 256         |     5.439 μs |   0.1879 μs |   0.2754 μs |  0.12 |    0.01 |  0.0458 |       - |       - |    1.44 KB |        0.72 |
|             |             |              |             |             |       |         |         |         |         |            |             |
| **MQTTnet**     | **1024**        |    **45.319 μs** |   **1.1799 μs** |   **1.7660 μs** |  **1.00** |    **0.05** |  **0.1221** |       **-** |       **-** |    **3.49 KB** |        **1.00** |
| Mqtt.Client | 1024        |     7.454 μs |   0.4139 μs |   0.6195 μs |  0.16 |    0.01 |  0.0916 |       - |       - |    2.95 KB |        0.85 |
|             |             |              |             |             |       |         |         |         |         |            |             |
| **MQTTnet**     | **4096**        |    **43.410 μs** |   **0.8438 μs** |   **1.0362 μs** |  **1.00** |    **0.03** |  **0.3052** |       **-** |       **-** |    **9.47 KB** |        **1.00** |
| Mqtt.Client | 4096        |    15.725 μs |   0.8282 μs |   1.2396 μs |  0.36 |    0.03 |  0.3052 |       - |       - |    9.11 KB |        0.96 |
|             |             |              |             |             |       |         |         |         |         |            |             |
| **MQTTnet**     | **16384**       |    **42.766 μs** |   **0.8369 μs** |   **1.0881 μs** |  **1.00** |    **0.04** |  **1.1597** |       **-** |       **-** |   **33.48 KB** |        **1.00** |
| Mqtt.Client | 16384       |    61.968 μs |   4.9396 μs |   7.3933 μs |  1.45 |    0.17 |  1.0986 |       - |       - |   33.19 KB |        0.99 |
|             |             |              |             |             |       |         |         |         |         |            |             |
| **MQTTnet**     | **65536**       |   **223.629 μs** |  **23.9651 μs** |  **35.8698 μs** |  **1.03** |    **0.24** |  **4.8828** |  **0.9766** |       **-** |  **130.24 KB** |        **1.00** |
| Mqtt.Client | 65536       |   207.290 μs |  20.8309 μs |  31.1787 μs |  0.95 |    0.21 |  3.9063 |  0.4883 |       - |  129.11 KB |        0.99 |
|             |             |              |             |             |       |         |         |         |         |            |             |
| **MQTTnet**     | **1048576**     | **3,387.447 μs** | **182.5257 μs** | **273.1959 μs** |  **1.01** |    **0.11** | **70.3125** | **70.3125** | **70.3125** | **2064.18 KB** |        **1.00** |
| Mqtt.Client | 1048576     | 2,900.521 μs |  95.1758 μs | 142.4546 μs |  0.86 |    0.08 | 73.2422 | 73.2422 | 73.2422 | 2063.96 KB |        1.00 |

## Mqtt.Client.Benchmarks.PublishQoS1Benchmark-report-github

**End-to-end.** Times a single `PublishAsync` at QoS 1 (at-least-once) per invocation, for each `PayloadSize`. The call completes only once the broker's PUBACK arrives, so each measurement includes one network round-trip plus the packet-id allocation and ack-correlation work.

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  Job-POFKUY : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

OutlierMode=RemoveUpper  MaxIterationCount=30  MinIterationCount=15  
WarmupCount=5  

```
| Method      | PayloadSize | Mean        | Error      | StdDev     | Ratio | RatioSD | Gen0    | Gen1    | Gen2    | Allocated | Alloc Ratio |
|------------ |------------ |------------:|-----------:|-----------:|------:|--------:|--------:|--------:|--------:|----------:|------------:|
| **MQTTnet**     | **64**          |   **108.46 μs** |   **3.968 μs** |   **5.939 μs** |  **1.00** |    **0.08** |       **-** |       **-** |       **-** |    **3866 B** |        **1.00** |
| Mqtt.Client | 64          |    85.73 μs |   2.613 μs |   3.910 μs |  0.79 |    0.06 |       - |       - |       - |    2560 B |        0.66 |
|             |             |             |            |            |       |         |         |         |         |           |             |
| **MQTTnet**     | **256**         |   **112.57 μs** |   **4.936 μs** |   **6.920 μs** |  **1.00** |    **0.08** |       **-** |       **-** |       **-** |    **4257 B** |        **1.00** |
| Mqtt.Client | 256         |    98.90 μs |   5.781 μs |   8.653 μs |  0.88 |    0.09 |       - |       - |       - |    2948 B |        0.69 |
|             |             |             |            |            |       |         |         |         |         |           |             |
| **MQTTnet**     | **1024**        |   **113.14 μs** |   **2.792 μs** |   **3.821 μs** |  **1.00** |    **0.05** |       **-** |       **-** |       **-** |    **5785 B** |        **1.00** |
| Mqtt.Client | 1024        |   111.10 μs |   6.226 μs |   9.318 μs |  0.98 |    0.09 |       - |       - |       - |    4480 B |        0.77 |
|             |             |             |            |            |       |         |         |         |         |           |             |
| **MQTTnet**     | **4096**        |   **142.21 μs** |   **6.773 μs** |   **9.927 μs** |  **1.00** |    **0.10** |  **0.2441** |       **-** |       **-** |   **12007 B** |        **1.00** |
| Mqtt.Client | 4096        |   139.94 μs |   6.336 μs |   9.484 μs |  0.99 |    0.09 |  0.2441 |       - |       - |   10696 B |        0.89 |
|             |             |             |            |            |       |         |         |         |         |           |             |
| **MQTTnet**     | **16384**       |   **154.89 μs** |   **6.382 μs** |   **9.553 μs** |  **1.00** |    **0.09** |  **1.2207** |       **-** |       **-** |   **36795 B** |        **1.00** |
| Mqtt.Client | 16384       |   148.12 μs |   9.322 μs |  13.369 μs |  0.96 |    0.10 |  0.9766 |       - |       - |   35488 B |        0.96 |
|             |             |             |            |            |       |         |         |         |         |           |             |
| **MQTTnet**     | **65536**       |   **203.68 μs** |   **8.598 μs** |  **11.769 μs** |  **1.00** |    **0.08** |  **4.8828** |  **0.4883** |       **-** |  **135987 B** |        **1.00** |
| Mqtt.Client | 65536       |   208.01 μs |   5.263 μs |   6.843 μs |  1.02 |    0.07 |  4.8828 |  0.4883 |       - |  134656 B |        0.99 |
|             |             |             |            |            |       |         |         |         |         |           |             |
| **MQTTnet**     | **1048576**     | **3,046.84 μs** | **145.021 μs** | **217.061 μs** |  **1.00** |    **0.10** | **66.4063** | **66.4063** | **66.4063** | **2120460 B** |        **1.00** |
| Mqtt.Client | 1048576     | 3,577.40 μs | 175.692 μs | 262.968 μs |  1.18 |    0.12 | 66.4063 | 66.4063 | 66.4063 |         - |        0.00 |

## Mqtt.Client.Benchmarks.PublishQoS2Benchmark-report-github

**End-to-end.** Times a single `PublishAsync` at QoS 2 (exactly-once) per invocation, for each `PayloadSize`. This drives the full four-packet handshake — PUBLISH → PUBREC → PUBREL → PUBCOMP, i.e. two round-trips — making it the most expensive delivery guarantee to benchmark.

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  Job-POFKUY : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

OutlierMode=RemoveUpper  MaxIterationCount=30  MinIterationCount=15  
WarmupCount=5  

```
| Method      | PayloadSize | Mean       | Error     | StdDev    | Ratio | RatioSD | Gen0    | Gen1    | Gen2    | Allocated  | Alloc Ratio |
|------------ |------------ |-----------:|----------:|----------:|------:|--------:|--------:|--------:|--------:|-----------:|------------:|
| **MQTTnet**     | **64**          |   **209.9 μs** |  **10.22 μs** |  **14.66 μs** |  **1.00** |    **0.10** |       **-** |       **-** |       **-** |    **6.63 KB** |        **1.00** |
| Mqtt.Client | 64          |   163.9 μs |   5.84 μs |   8.74 μs |  0.78 |    0.07 |       - |       - |       - |    4.01 KB |        0.61 |
|             |             |            |           |           |       |         |         |         |         |            |             |
| **MQTTnet**     | **256**         |   **183.4 μs** |   **4.27 μs** |   **5.85 μs** |  **1.00** |    **0.04** |       **-** |       **-** |       **-** |    **6.99 KB** |        **1.00** |
| Mqtt.Client | 256         |   163.5 μs |   5.27 μs |   7.88 μs |  0.89 |    0.05 |       - |       - |       - |    4.39 KB |        0.63 |
|             |             |            |           |           |       |         |         |         |         |            |             |
| **MQTTnet**     | **1024**        |   **197.9 μs** |  **11.94 μs** |  **17.87 μs** |  **1.01** |    **0.12** |       **-** |       **-** |       **-** |    **8.49 KB** |        **1.00** |
| Mqtt.Client | 1024        |   157.9 μs |   3.90 μs |   5.59 μs |  0.80 |    0.07 |       - |       - |       - |    5.89 KB |        0.69 |
|             |             |            |           |           |       |         |         |         |         |            |             |
| **MQTTnet**     | **4096**        |   **187.5 μs** |   **3.64 μs** |   **5.11 μs** |  **1.00** |    **0.04** |  **0.4883** |       **-** |       **-** |   **14.56 KB** |        **1.00** |
| Mqtt.Client | 4096        |   185.0 μs |   8.43 μs |  12.36 μs |  0.99 |    0.07 |       - |       - |       - |   11.96 KB |        0.82 |
|             |             |            |           |           |       |         |         |         |         |            |             |
| **MQTTnet**     | **16384**       |   **228.5 μs** |  **14.48 μs** |  **21.67 μs** |  **1.01** |    **0.13** |  **0.9766** |       **-** |       **-** |   **38.77 KB** |        **1.00** |
| Mqtt.Client | 16384       |   215.8 μs |  15.25 μs |  22.83 μs |  0.95 |    0.13 |  0.9766 |       - |       - |   36.17 KB |        0.93 |
|             |             |            |           |           |       |         |         |         |         |            |             |
| **MQTTnet**     | **65536**       |   **259.2 μs** |  **11.27 μs** |  **16.88 μs** |  **1.00** |    **0.09** |  **4.8828** |  **0.9766** |       **-** |  **135.63 KB** |        **1.00** |
| Mqtt.Client | 65536       |   271.8 μs |  12.79 μs |  19.14 μs |  1.05 |    0.10 |  4.8828 |  0.9766 |       - |  133.02 KB |        0.98 |
|             |             |            |           |           |       |         |         |         |         |            |             |
| **MQTTnet**     | **1048576**     | **3,428.5 μs** | **302.56 μs** | **433.93 μs** |  **1.01** |    **0.17** | **66.4063** | **66.4063** | **66.4063** | **2073.26 KB** |        **1.00** |
| Mqtt.Client | 1048576     | 3,330.8 μs | 224.87 μs | 329.61 μs |  0.99 |    0.15 | 66.4063 | 66.4063 | 66.4063 | 2070.76 KB |        1.00 |

## Mqtt.Client.Benchmarks.SubscribeReceiveBenchmark-report-github

**End-to-end.** Measures receive throughput: a separate publisher connection publishes one QoS 0 message and the subscribed client-under-test waits to read it from its channel, for each `PayloadSize`. The application messages are pre-built during setup, so only publish → broker fan-out → client receive/dispatch is timed.

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  Job-POFKUY : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

OutlierMode=RemoveUpper  MaxIterationCount=30  MinIterationCount=15  
WarmupCount=5  

```
| Method                | PayloadSize | Mean       | Error     | StdDev    | Ratio | RatioSD | Gen0     | Gen1     | Gen2     | Allocated  | Alloc Ratio |
|---------------------- |------------ |-----------:|----------:|----------:|------:|--------:|---------:|---------:|---------:|-----------:|------------:|
| **&#39;MQTTnet receive&#39;**     | **64**          |   **143.2 μs** |  **13.27 μs** |  **19.86 μs** |  **1.02** |    **0.19** |        **-** |        **-** |        **-** |    **4.87 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 64          |   162.2 μs |   9.88 μs |  14.79 μs |  1.15 |    0.18 |        - |        - |        - |    3.33 KB |        0.68 |
|                       |             |            |           |           |       |         |          |          |          |            |             |
| **&#39;MQTTnet receive&#39;**     | **256**         |   **171.9 μs** |  **11.18 μs** |  **16.73 μs** |  **1.01** |    **0.14** |        **-** |        **-** |        **-** |    **5.54 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 256         |   179.2 μs |  14.92 μs |  22.33 μs |  1.05 |    0.16 |        - |        - |        - |    3.88 KB |        0.70 |
|                       |             |            |           |           |       |         |          |          |          |            |             |
| **&#39;MQTTnet receive&#39;**     | **1024**        |   **171.3 μs** |  **13.29 μs** |  **19.89 μs** |  **1.01** |    **0.17** |   **0.2441** |        **-** |        **-** |    **8.45 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 1024        |   153.5 μs |   8.13 μs |  11.91 μs |  0.91 |    0.13 |        - |        - |        - |    6.13 KB |        0.73 |
|                       |             |            |           |           |       |         |          |          |          |            |             |
| **&#39;MQTTnet receive&#39;**     | **4096**        |   **165.6 μs** |   **4.76 μs** |   **6.68 μs** |  **1.00** |    **0.06** |   **0.4883** |        **-** |        **-** |   **20.59 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 4096        |   190.8 μs |  10.59 μs |  15.84 μs |  1.15 |    0.10 |   0.4883 |        - |        - |   15.21 KB |        0.74 |
|                       |             |            |           |           |       |         |          |          |          |            |             |
| **&#39;MQTTnet receive&#39;**     | **16384**       |   **313.4 μs** |  **25.14 μs** |  **34.41 μs** |  **1.01** |    **0.15** |   **2.4414** |        **-** |        **-** |   **69.07 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 16384       |   227.7 μs |  14.42 μs |  21.58 μs |  0.73 |    0.10 |   1.4648 |        - |        - |   51.42 KB |        0.74 |
|                       |             |            |           |           |       |         |          |          |          |            |             |
| **&#39;MQTTnet receive&#39;**     | **65536**       |   **608.5 μs** |  **64.69 μs** |  **96.83 μs** |  **1.02** |    **0.23** |   **8.7891** |   **1.9531** |        **-** |  **262.88 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 65536       |   311.4 μs |  14.13 μs |  21.15 μs |  0.52 |    0.09 |   6.8359 |   1.4648 |        - |  196.29 KB |        0.75 |
|                       |             |            |           |           |       |         |          |          |          |            |             |
| **&#39;MQTTnet receive&#39;**     | **1048576**     | **9,912.6 μs** | **638.20 μs** | **955.22 μs** |  **1.01** |    **0.13** | **125.0000** | **125.0000** | **125.0000** | **4138.22 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 1048576     | 6,294.3 μs | 594.38 μs | 889.64 μs |  0.64 |    0.11 | 101.5625 | 101.5625 | 101.5625 | 3094.35 KB |        0.75 |

<!-- BEGIN: cross-language throughput (--crosslang) -->
## Cross-implementation throughput — Mqtt.Client vs MQTTnet vs C (Mosquitto, Paho)

_Cross-language section generated 2026-06-26 07:38 UTC by `--crosslang`._

End-to-end **publish→receive** throughput: each publisher sends N messages through a real [Eclipse Mosquitto](https://mosquitto.org/) broker to a constant `mosquitto_sub -C N` subscriber; the rate is N divided by the wall-clock time for the subscriber to receive all N. Two native-C datapoints are included — the `mosquitto_pub` CLI tool and a purpose-built **paho.mqtt.c** publisher. Higher is better.

These numbers are **wall-clock and cross-language** — not directly comparable to the per-operation BenchmarkDotNet results above. The **Mosquitto C (CLI)** column is the `mosquitto_pub` command-line tool, driven by feeding it one message per line on stdin; that stdin mechanism — not the protocol — caps it at roughly 14k msg/s here for both QoS levels. It still pipelines QoS 1 (it sends the PUBLISHes and collects the PUBACKs asynchronously, never blocking per message), so its QoS 1 lands at the same stdin-bound ceiling rather than below it — a convenience tool, not a throughput-optimised client. The **Paho C (lib)** column is a purpose-built publisher on the Eclipse Paho C synchronous `MQTTClient` v5 API doing exactly what the .NET clients do — one persistent connection over the same broker — so it is the true apples-to-apples native baseline. For QoS 1 every persistent publisher keeps a sliding window of in-flight publishes (the .NET clients and paho up to 100; `mosquitto_pub` via libmosquitto's own window) and collects the PUBACKs asynchronously, so the figure is sustained throughput, not per-message round-trip latency; QoS 0 is measured end-to-end (the subscriber must receive all N), so fire-and-forget enqueue is not mistaken for delivery.

**Reading the Paho C column:** its QoS 0 figure (no acknowledgements) is competitive, but its QoS 1 figure is held back by paho.mqtt.c exposing no `TCP_NODELAY` option — its acknowledgement round-trips stall on Nagle / delayed-ACK even when pipelined, where the .NET clients disable Nagle. So the QoS 1 number reflects paho's default TCP behaviour, not a native-C ceiling — a useful reminder that TCP tuning, not language, dominates QoS 1 throughput here.

### QoS 0 — throughput (msg/s, higher is better)

| Payload | Mqtt.Client | MQTTnet | Mosquitto C (CLI) | Paho C (lib) |
| --- | ---: | ---: | ---: | ---: |
| 64 B | 69,250 | 37,660 | 13,927 | 22,479 |
| 256 B | 98,964 | 37,977 | 14,127 | 22,508 |
| 1 KiB | 93,281 | 69,843 | 9,266 | 15,582 |
| 16 KiB | 16,647 | 11,741 | 3,550 | 5,549 |
| 64 KiB | 7,488 | 2,237 | 851 | 590 |

### QoS 1 — throughput (msg/s, higher is better)

| Payload | Mqtt.Client | MQTTnet | Mosquitto C (CLI) | Paho C (lib) |
| --- | ---: | ---: | ---: | ---: |
| 64 B | 32,806 | 12,302 | 11,670 | 953 |
| 256 B | 29,849 | 12,143 | 14,285 | 953 |
| 1 KiB | 26,288 | 12,511 | 6,525 | 935 |
| 16 KiB | 12,882 | 223 | 3,409 | 865 |
| 64 KiB | 3,219 | 111 | 836 | 633 |

<!-- END: cross-language throughput -->
