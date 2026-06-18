# Mqtt.Client vs MQTTnet — benchmark results

_Generated 2026-06-18 16:06 UTC._

[MQTTnet](https://github.com/dotnet/MQTTnet) is a mature, battle-tested .NET MQTT library.
These benchmarks are not a verdict on MQTTnet — they exist to make tradeoffs visible
for callers choosing between the two clients. See the README's
"When to pick MQTTnet instead" section for guidance.

Run with:
```
dotnet run -c Release --project tests/Mqtt.Client.Benchmarks -- --filter '*' --report
dotnet run -c Release --project tests/Mqtt.Client.Benchmarks -- --filter '*' --full --report
```

## Mqtt.Client.Benchmarks.ConnectLatencyBenchmark-report-github

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]   : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method      | Mean     | Error    | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------ |---------:|---------:|----------:|------:|--------:|----------:|------------:|
| MQTTnet     | 4.150 ms | 4.768 ms | 0.2613 ms |  1.00 |    0.08 |  37.32 KB |        1.00 |
| Mqtt.Client | 4.524 ms | 7.956 ms | 0.4361 ms |  1.09 |    0.11 |  49.69 KB |        1.33 |

## Mqtt.Client.Benchmarks.DecodePublishBenchmark-report-github

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]   : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method      | PayloadSize | Mean         | Error       | StdDev     | Ratio | RatioSD | Gen0   | Gen1   | Gen2   | Allocated | Alloc Ratio |
|------------ |------------ |-------------:|------------:|-----------:|------:|--------:|-------:|-------:|-------:|----------:|------------:|
| **MQTTnet**     | **64**          |     **96.92 ns** |    **86.18 ns** |   **4.724 ns** |  **1.00** |    **0.06** | **0.0082** |      **-** |      **-** |     **248 B** |        **1.00** |
| Mqtt.Client | 64          |    168.64 ns |    97.17 ns |   5.326 ns |  1.74 |    0.09 | 0.0069 | 0.0002 | 0.0002 |         - |        0.00 |
|             |             |              |             |            |       |         |        |        |        |           |             |
| **MQTTnet**     | **256**         |    **120.85 ns** |    **66.15 ns** |   **3.626 ns** |  **1.00** |    **0.04** | **0.0145** |      **-** |      **-** |     **440 B** |        **1.00** |
| Mqtt.Client | 256         |    219.74 ns |   210.55 ns |  11.541 ns |  1.82 |    0.10 | 0.0129 |      - |      - |     392 B |        0.89 |
|             |             |              |             |            |       |         |        |        |        |           |             |
| **MQTTnet**     | **1024**        |    **249.66 ns** |    **49.55 ns** |   **2.716 ns** |  **1.00** |    **0.01** | **0.0529** | **0.0002** | **0.0002** |         **-** |          **NA** |
| Mqtt.Client | 1024        |    324.05 ns |    84.48 ns |   4.631 ns |  1.30 |    0.02 | 0.0386 |      - |      - |    1160 B |          NA |
|             |             |              |             |            |       |         |        |        |        |           |             |
| **MQTTnet**     | **4096**        |    **885.79 ns** | **1,374.27 ns** |  **75.329 ns** |  **1.00** |    **0.11** | **0.1440** |      **-** |      **-** |    **4280 B** |        **1.00** |
| Mqtt.Client | 4096        |    902.73 ns |   686.03 ns |  37.604 ns |  1.02 |    0.08 | 0.1402 |      - |      - |    4232 B |        0.99 |
|             |             |              |             |            |       |         |        |        |        |           |             |
| **MQTTnet**     | **16384**       |  **2,608.81 ns** | **1,933.98 ns** | **106.008 ns** |  **1.00** |    **0.05** | **0.7477** |      **-** |      **-** |   **16568 B** |        **1.00** |
| Mqtt.Client | 16384       |  3,227.56 ns | 5,116.69 ns | 280.463 ns |  1.24 |    0.10 | 0.5531 |      - |      - |   16520 B |        1.00 |
|             |             |              |             |            |       |         |        |        |        |           |             |
| **MQTTnet**     | **65536**       |  **8,725.43 ns** | **8,408.84 ns** | **460.917 ns** |  **1.00** |    **0.07** | **2.5482** |      **-** |      **-** |   **65720 B** |        **1.00** |
| Mqtt.Client | 65536       | 11,636.65 ns | 3,237.46 ns | 177.456 ns |  1.34 |    0.07 | 2.6855 |      - |      - |   65672 B |        1.00 |

## Mqtt.Client.Benchmarks.EncodePublishBenchmark-report-github

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]   : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method      | PayloadSize | Mean      | Error      | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Gen2   | Allocated | Alloc Ratio |
|------------ |------------ |----------:|-----------:|---------:|------:|--------:|-------:|-------:|-------:|----------:|------------:|
| **MQTTnet**     | **64**          |  **51.78 ns** |  **34.310 ns** | **1.881 ns** |  **1.00** |    **0.04** |      **-** |      **-** |      **-** |         **-** |          **NA** |
| Mqtt.Client | 64          | 117.78 ns |  43.949 ns | 2.409 ns |  2.28 |    0.08 | 0.0020 |      - |      - |      64 B |          NA |
|             |             |           |            |          |       |         |        |        |        |           |             |
| **MQTTnet**     | **256**         |  **34.60 ns** |   **2.434 ns** | **0.133 ns** |  **1.00** |    **0.00** |      **-** |      **-** |      **-** |         **-** |          **NA** |
| Mqtt.Client | 256         |  77.62 ns |  16.392 ns | 0.899 ns |  2.24 |    0.02 | 0.0027 | 0.0001 | 0.0001 |         - |          NA |
|             |             |           |            |          |       |         |        |        |        |           |             |
| **MQTTnet**     | **1024**        |  **35.70 ns** |   **4.834 ns** | **0.265 ns** |  **1.00** |    **0.01** |      **-** |      **-** |      **-** |         **-** |          **NA** |
| Mqtt.Client | 1024        |  85.38 ns |  35.760 ns | 1.960 ns |  2.39 |    0.05 | 0.0036 |      - |      - |      64 B |          NA |
|             |             |           |            |          |       |         |        |        |        |           |             |
| **MQTTnet**     | **4096**        |  **36.31 ns** |  **20.777 ns** | **1.139 ns** |  **1.00** |    **0.04** |      **-** |      **-** |      **-** |         **-** |          **NA** |
| Mqtt.Client | 4096        |  86.69 ns |  77.822 ns | 4.266 ns |  2.39 |    0.12 | 0.0020 |      - |      - |      64 B |          NA |
|             |             |           |            |          |       |         |        |        |        |           |             |
| **MQTTnet**     | **16384**       |  **35.34 ns** |   **3.877 ns** | **0.212 ns** |  **1.00** |    **0.01** |      **-** |      **-** |      **-** |         **-** |          **NA** |
| Mqtt.Client | 16384       |  92.63 ns | 164.351 ns | 9.009 ns |  2.62 |    0.22 | 0.0027 | 0.0001 | 0.0001 |         - |          NA |
|             |             |           |            |          |       |         |        |        |        |           |             |
| **MQTTnet**     | **65536**       |  **40.46 ns** |  **56.936 ns** | **3.121 ns** |  **1.00** |    **0.09** |      **-** |      **-** |      **-** |         **-** |          **NA** |
| Mqtt.Client | 65536       |  82.58 ns |  43.201 ns | 2.368 ns |  2.05 |    0.14 | 0.0020 |      - |      - |      64 B |          NA |

## Mqtt.Client.Benchmarks.EncodeSubscribeBenchmark-report-github

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]   : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method      | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------ |----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| MQTTnet     |  56.24 ns | 18.85 ns | 1.033 ns |  1.00 |    0.02 |      - |         - |          NA |
| Mqtt.Client | 107.14 ns | 44.74 ns | 2.452 ns |  1.91 |    0.05 | 0.0042 |      96 B |          NA |

## Mqtt.Client.Benchmarks.PublishQoS0Benchmark-report-github

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]   : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method      | PayloadSize | Mean       | Error      | StdDev     | Ratio | RatioSD | Gen0   | Gen1   | Gen2   | Allocated | Alloc Ratio |
|------------ |------------ |-----------:|-----------:|-----------:|------:|--------:|-------:|-------:|-------:|----------:|------------:|
| **MQTTnet**     | **64**          |  **54.091 μs** |  **30.043 μs** |  **1.6467 μs** |  **1.00** |    **0.04** |      **-** |      **-** |      **-** |    **1669 B** |        **1.00** |
| Mqtt.Client | 64          |   4.308 μs |   5.493 μs |  0.3011 μs |  0.08 |    0.01 | 0.0229 |      - |      - |    1015 B |        0.61 |
|             |             |            |            |            |       |         |        |        |        |           |             |
| **MQTTnet**     | **256**         |  **41.965 μs** |   **6.933 μs** |  **0.3800 μs** |  **1.00** |    **0.01** | **0.0610** |      **-** |      **-** |    **2044 B** |        **1.00** |
| Mqtt.Client | 256         |   5.567 μs |   4.907 μs |  0.2690 μs |  0.13 |    0.01 | 0.0305 |      - |      - |    1349 B |        0.66 |
|             |             |            |            |            |       |         |        |        |        |           |             |
| **MQTTnet**     | **1024**        |  **45.203 μs** |  **37.233 μs** |  **2.0409 μs** |  **1.00** |    **0.05** | **0.0610** |      **-** |      **-** |    **3553 B** |        **1.00** |
| Mqtt.Client | 1024        |   6.554 μs |   7.626 μs |  0.4180 μs |  0.15 |    0.01 | 0.0916 |      - |      - |    2997 B |        0.84 |
|             |             |            |            |            |       |         |        |        |        |           |             |
| **MQTTnet**     | **4096**        |  **47.728 μs** |  **24.901 μs** |  **1.3649 μs** |  **1.00** |    **0.03** | **0.3052** |      **-** |      **-** |    **9660 B** |        **1.00** |
| Mqtt.Client | 4096        |  25.753 μs |  15.854 μs |  0.8690 μs |  0.54 |    0.02 | 0.3662 | 0.1526 | 0.0610 |   10875 B |        1.13 |
|             |             |            |            |            |       |         |        |        |        |           |             |
| **MQTTnet**     | **16384**       |  **44.594 μs** |  **76.730 μs** |  **4.2058 μs** |  **1.01** |    **0.11** | **1.1597** |      **-** |      **-** |   **34302 B** |        **1.00** |
| Mqtt.Client | 16384       |  50.118 μs |  77.919 μs |  4.2710 μs |  1.13 |    0.12 | 1.1597 |      - |      - |   34317 B |        1.00 |
|             |             |            |            |            |       |         |        |        |        |           |             |
| **MQTTnet**     | **65536**       | **189.548 μs** | **427.349 μs** | **23.4245 μs** |  **1.01** |    **0.15** | **5.8594** | **1.4648** |      **-** |  **133353 B** |        **1.00** |
| Mqtt.Client | 65536       | 152.592 μs | 152.713 μs |  8.3707 μs |  0.81 |    0.09 | 4.6387 | 1.2207 |      - |  133793 B |        1.00 |

## Mqtt.Client.Benchmarks.PublishQoS1Benchmark-report-github

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]   : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method      | PayloadSize | Mean     | Error     | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------ |------------ |---------:|----------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| **MQTTnet**     | **64**          | **122.2 μs** |  **91.32 μs** |  **5.01 μs** |  **1.00** |    **0.05** |      **-** |      **-** |   **3.79 KB** |        **1.00** |
| Mqtt.Client | 64          | 102.2 μs |  27.06 μs |  1.48 μs |  0.84 |    0.03 |      - |      - |   2.53 KB |        0.67 |
|             |             |          |           |          |       |         |        |        |           |             |
| **MQTTnet**     | **256**         | **115.4 μs** |  **84.41 μs** |  **4.63 μs** |  **1.00** |    **0.05** |      **-** |      **-** |   **4.16 KB** |        **1.00** |
| Mqtt.Client | 256         | 100.3 μs |  54.73 μs |  3.00 μs |  0.87 |    0.04 |      - |      - |   2.91 KB |        0.70 |
|             |             |          |           |          |       |         |        |        |           |             |
| **MQTTnet**     | **1024**        | **130.4 μs** | **101.17 μs** |  **5.55 μs** |  **1.00** |    **0.05** |      **-** |      **-** |   **5.66 KB** |        **1.00** |
| Mqtt.Client | 1024        | 101.5 μs |  47.06 μs |  2.58 μs |  0.78 |    0.03 |      - |      - |   4.41 KB |        0.78 |
|             |             |          |           |          |       |         |        |        |           |             |
| **MQTTnet**     | **4096**        | **136.7 μs** |   **2.02 μs** |  **0.11 μs** |  **1.00** |    **0.00** | **0.2441** |      **-** |  **11.72 KB** |        **1.00** |
| Mqtt.Client | 4096        | 130.4 μs |  93.31 μs |  5.11 μs |  0.95 |    0.03 | 0.2441 |      - |  10.48 KB |        0.89 |
|             |             |          |           |          |       |         |        |        |           |             |
| **MQTTnet**     | **16384**       | **143.0 μs** | **142.96 μs** |  **7.84 μs** |  **1.00** |    **0.07** | **1.2207** |      **-** |  **35.93 KB** |        **1.00** |
| Mqtt.Client | 16384       | 157.9 μs |  40.19 μs |  2.20 μs |  1.11 |    0.05 | 1.2207 |      - |  34.71 KB |        0.97 |
|             |             |          |           |          |       |         |        |        |           |             |
| **MQTTnet**     | **65536**       | **222.7 μs** | **267.72 μs** | **14.67 μs** |  **1.00** |    **0.08** | **4.8828** | **0.9766** |  **132.8 KB** |        **1.00** |
| Mqtt.Client | 65536       | 246.0 μs | 282.29 μs | 15.47 μs |  1.11 |    0.09 | 4.8828 | 0.9766 | 131.64 KB |        0.99 |

## Mqtt.Client.Benchmarks.PublishQoS2Benchmark-report-github

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]   : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method      | PayloadSize | Mean     | Error     | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Gen2   | Allocated | Alloc Ratio |
|------------ |------------ |---------:|----------:|---------:|------:|--------:|-------:|-------:|-------:|----------:|------------:|
| **MQTTnet**     | **64**          | **238.0 μs** | **120.89 μs** |  **6.63 μs** |  **1.00** |    **0.03** |      **-** |      **-** |      **-** |   **6.63 KB** |        **1.00** |
| Mqtt.Client | 64          | 202.8 μs | 170.55 μs |  9.35 μs |  0.85 |    0.04 |      - |      - |      - |   4.08 KB |        0.61 |
|             |             |          |           |          |       |         |        |        |        |           |             |
| **MQTTnet**     | **256**         | **217.2 μs** | **246.15 μs** | **13.49 μs** |  **1.00** |    **0.08** |      **-** |      **-** |      **-** |      **7 KB** |        **1.00** |
| Mqtt.Client | 256         | 197.0 μs | 148.14 μs |  8.12 μs |  0.91 |    0.06 |      - |      - |      - |   4.45 KB |        0.64 |
|             |             |          |           |          |       |         |        |        |        |           |             |
| **MQTTnet**     | **1024**        | **223.4 μs** | **360.17 μs** | **19.74 μs** |  **1.01** |    **0.11** |      **-** |      **-** |      **-** |    **8.5 KB** |        **1.00** |
| Mqtt.Client | 1024        | 214.2 μs |  39.50 μs |  2.17 μs |  0.96 |    0.08 |      - |      - |      - |   5.95 KB |        0.70 |
|             |             |          |           |          |       |         |        |        |        |           |             |
| **MQTTnet**     | **4096**        | **237.0 μs** | **240.89 μs** | **13.20 μs** |  **1.00** |    **0.07** | **0.4883** |      **-** |      **-** |  **14.56 KB** |        **1.00** |
| Mqtt.Client | 4096        | 246.6 μs | 613.85 μs | 33.65 μs |  1.04 |    0.13 |      - |      - |      - |  12.03 KB |        0.83 |
|             |             |          |           |          |       |         |        |        |        |           |             |
| **MQTTnet**     | **16384**       | **233.7 μs** | **190.94 μs** | **10.47 μs** |  **1.00** |    **0.05** | **0.9766** |      **-** |      **-** |  **38.77 KB** |        **1.00** |
| Mqtt.Client | 16384       | 254.6 μs | 214.26 μs | 11.74 μs |  1.09 |    0.06 | 0.9766 |      - |      - |  36.24 KB |        0.93 |
|             |             |          |           |          |       |         |        |        |        |           |             |
| **MQTTnet**     | **65536**       | **346.2 μs** | **209.19 μs** | **11.47 μs** |  **1.00** |    **0.04** | **4.8828** | **0.4883** |      **-** | **135.64 KB** |        **1.00** |
| Mqtt.Client | 65536       | 455.6 μs |   9.10 μs |  18.39 μs |  1.17 |    0.10 | 4.8828 |      - |      - | 133.36 KB |        0.98 |

> Note: the 65 536 B QoS 2 row was re-measured with the full (DefaultJob) configuration — the
> ShortRun pass produced a noisy allocation outlier (≈ 356 KB / 2.6×) caused by background
> read/write-loop and in-process-broker allocations being attributed to that 35 %-variance cell.
> The stable figure is **133 KB (0.98×)**, in line with QoS 0/QoS 1 at the same payload.

## Mqtt.Client.Benchmarks.SubscribeReceiveBenchmark-report-github

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]   : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                | PayloadSize | Mean     | Error       | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|---------------------- |------------ |---------:|------------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| **&#39;MQTTnet receive&#39;**     | **64**          | **137.4 μs** |   **204.41 μs** | **11.20 μs** |  **1.00** |    **0.10** |      **-** |      **-** |    **4.7 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 64          | 153.7 μs |   139.22 μs |  7.63 μs |  1.12 |    0.09 |      - |      - |   3.36 KB |        0.71 |
|                       |             |          |             |          |       |         |        |        |           |             |
| **&#39;MQTTnet receive&#39;**     | **256**         | **133.9 μs** |    **65.85 μs** |  **3.61 μs** |  **1.00** |    **0.03** |      **-** |      **-** |   **5.44 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 256         | 136.2 μs |   145.51 μs |  7.98 μs |  1.02 |    0.06 |      - |      - |   3.92 KB |        0.72 |
|                       |             |          |             |          |       |         |        |        |           |             |
| **&#39;MQTTnet receive&#39;**     | **1024**        | **156.2 μs** |    **63.00 μs** |  **3.45 μs** |  **1.00** |    **0.03** | **0.2441** |      **-** |   **8.44 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 1024        | 144.3 μs |    74.48 μs |  4.08 μs |  0.92 |    0.03 |      - |      - |   6.17 KB |        0.73 |
|                       |             |          |             |          |       |         |        |        |           |             |
| **&#39;MQTTnet receive&#39;**     | **4096**        | **190.0 μs** |   **170.10 μs** |  **9.32 μs** |  **1.00** |    **0.06** | **0.4883** |      **-** |  **20.61 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 4096        | 210.0 μs |   196.31 μs | 10.76 μs |  1.11 |    0.07 | 0.4883 |      - |  15.24 KB |        0.74 |
|                       |             |          |             |          |       |         |        |        |           |             |
| **&#39;MQTTnet receive&#39;**     | **16384**       | **427.0 μs** |   **498.19 μs** | **27.31 μs** |  **1.00** |    **0.08** | **1.9531** |      **-** |  **69.09 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 16384       | 250.1 μs |   593.09 μs | 32.51 μs |  0.59 |    0.07 | 1.4648 |      - |  51.46 KB |        0.74 |
|                       |             |          |             |          |       |         |        |        |           |             |
| **&#39;MQTTnet receive&#39;**     | **65536**       | **821.5 μs** | **1,526.33 μs** | **83.66 μs** |  **1.01** |    **0.12** | **9.7656** | **1.9531** | **262.89 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 65536       | 391.0 μs |   151.75 μs |  8.32 μs |  0.48 |    0.04 | 6.8359 | 0.9766 | 196.39 KB |        0.75 |

