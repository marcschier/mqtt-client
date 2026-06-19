# Mqtt.Client vs MQTTnet — benchmark results

_Generated 2026-06-19 16:05 UTC._

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
| Method      | Mean      | Error     | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------ |----------:|----------:|---------:|------:|--------:|----------:|------------:|
| MQTTnet     |  8.631 ms | 25.027 ms | 1.372 ms |  1.02 |    0.21 |  37.25 KB |        1.00 |
| Mqtt.Client | 24.905 ms | 48.998 ms | 2.686 ms |  2.94 |    0.52 |  48.46 KB |        1.30 |

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
| Method      | PayloadSize | Mean        | Error        | StdDev      | Ratio | RatioSD | Gen0   | Gen1   | Gen2   | Allocated | Alloc Ratio |
|------------ |------------ |------------:|-------------:|------------:|------:|--------:|-------:|-------:|-------:|----------:|------------:|
| **MQTTnet**     | **64**          |    **135.1 ns** |     **47.17 ns** |     **2.59 ns** |  **1.00** |    **0.02** | **0.0081** |      **-** |      **-** |     **248 B** |        **1.00** |
| Mqtt.Client | 64          |    210.3 ns |    137.90 ns |     7.56 ns |  1.56 |    0.06 | 0.0069 | 0.0002 | 0.0002 |         - |        0.00 |
|             |             |             |              |             |       |         |        |        |        |           |             |
| **MQTTnet**     | **256**         |    **230.9 ns** |    **364.56 ns** |    **19.98 ns** |  **1.01** |    **0.11** | **0.0145** |      **-** |      **-** |     **440 B** |        **1.00** |
| Mqtt.Client | 256         |    226.3 ns |     28.04 ns |     1.54 ns |  0.99 |    0.07 | 0.0129 |      - |      - |     392 B |        0.89 |
|             |             |             |              |             |       |         |        |        |        |           |             |
| **MQTTnet**     | **1024**        |    **367.9 ns** |    **244.13 ns** |    **13.38 ns** |  **1.00** |    **0.04** | **0.0401** |      **-** |      **-** |    **1208 B** |        **1.00** |
| Mqtt.Client | 1024        |    398.9 ns |    338.91 ns |    18.58 ns |  1.09 |    0.06 | 0.0386 |      - |      - |    1160 B |        0.96 |
|             |             |             |              |             |       |         |        |        |        |           |             |
| **MQTTnet**     | **4096**        |  **1,251.4 ns** |    **342.86 ns** |    **18.79 ns** |  **1.00** |    **0.02** | **0.1431** |      **-** |      **-** |    **4280 B** |        **1.00** |
| Mqtt.Client | 4096        |  1,187.2 ns |  1,324.62 ns |    72.61 ns |  0.95 |    0.05 | 0.1373 |      - |      - |    4232 B |        0.99 |
|             |             |             |              |             |       |         |        |        |        |           |             |
| **MQTTnet**     | **16384**       |  **3,385.3 ns** |  **1,074.77 ns** |    **58.91 ns** |  **1.00** |    **0.02** | **0.5760** |      **-** |      **-** |   **16568 B** |        **1.00** |
| Mqtt.Client | 16384       |  4,308.3 ns |  8,151.18 ns |   446.79 ns |  1.27 |    0.12 | 0.5493 |      - |      - |   16520 B |        1.00 |
|             |             |             |              |             |       |         |        |        |        |           |             |
| **MQTTnet**     | **65536**       | **11,918.5 ns** |  **4,961.66 ns** |   **271.97 ns** |  **1.00** |    **0.03** | **2.5482** |      **-** |      **-** |   **65720 B** |        **1.00** |
| Mqtt.Client | 65536       | 16,756.9 ns | 27,040.32 ns | 1,482.17 ns |  1.41 |    0.11 | 2.6855 |      - |      - |   65672 B |        1.00 |

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
| Method      | PayloadSize | Mean     | Error     | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------ |------------ |---------:|----------:|---------:|------:|--------:|----------:|------------:|
| **MQTTnet**     | **64**          | **51.13 ns** | **25.099 ns** | **1.376 ns** |  **1.00** |    **0.03** |         **-** |          **NA** |
| Mqtt.Client | 64          | 49.35 ns | 14.114 ns | 0.774 ns |  0.97 |    0.03 |         - |          NA |
|             |             |          |           |          |       |         |           |             |
| **MQTTnet**     | **256**         | **52.62 ns** | **19.995 ns** | **1.096 ns** |  **1.00** |    **0.03** |         **-** |          **NA** |
| Mqtt.Client | 256         | 77.17 ns | 28.487 ns | 1.561 ns |  1.47 |    0.04 |         - |          NA |
|             |             |          |           |          |       |         |           |             |
| **MQTTnet**     | **1024**        | **57.36 ns** | **34.760 ns** | **1.905 ns** |  **1.00** |    **0.04** |         **-** |          **NA** |
| Mqtt.Client | 1024        | 78.96 ns | 31.364 ns | 1.719 ns |  1.38 |    0.05 |         - |          NA |
|             |             |          |           |          |       |         |           |             |
| **MQTTnet**     | **4096**        | **59.02 ns** | **52.893 ns** | **2.899 ns** |  **1.00** |    **0.06** |         **-** |          **NA** |
| Mqtt.Client | 4096        | 75.96 ns | 99.455 ns | 5.451 ns |  1.29 |    0.10 |         - |          NA |
|             |             |          |           |          |       |         |           |             |
| **MQTTnet**     | **16384**       | **57.22 ns** | **62.282 ns** | **3.414 ns** |  **1.00** |    **0.07** |         **-** |          **NA** |
| Mqtt.Client | 16384       | 79.79 ns | 74.136 ns | 4.064 ns |  1.40 |    0.10 |         - |          NA |
|             |             |          |           |          |       |         |           |             |
| **MQTTnet**     | **65536**       | **60.59 ns** | **53.581 ns** | **2.937 ns** |  **1.00** |    **0.06** |         **-** |          **NA** |
| Mqtt.Client | 65536       | 80.89 ns |  6.831 ns | 0.374 ns |  1.34 |    0.06 |         - |          NA |

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
| Method      | Mean     | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------ |---------:|---------:|---------:|------:|--------:|----------:|------------:|
| MQTTnet     | 93.96 ns | 20.16 ns | 1.105 ns |  1.00 |    0.01 |         - |          NA |
| Mqtt.Client | 79.79 ns | 42.78 ns | 2.345 ns |  0.85 |    0.02 |         - |          NA |

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
| Method      | PayloadSize | Mean       | Error      | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Gen2   | Allocated | Alloc Ratio |
|------------ |------------ |-----------:|-----------:|----------:|------:|--------:|-------:|-------:|-------:|----------:|------------:|
| **MQTTnet**     | **64**          |  **53.433 μs** |  **36.099 μs** | **1.9787 μs** |  **1.00** |    **0.05** |      **-** |      **-** |      **-** |    **1679 B** |        **1.00** |
| Mqtt.Client | 64          |   5.416 μs |  16.724 μs | 0.9167 μs |  0.10 |    0.02 | 0.0153 |      - |      - |     899 B |        0.54 |
|             |             |            |            |           |       |         |        |        |        |           |             |
| **MQTTnet**     | **256**         |  **52.140 μs** |  **14.391 μs** | **0.7888 μs** |  **1.00** |    **0.02** | **0.0610** |      **-** |      **-** |    **2051 B** |        **1.00** |
| Mqtt.Client | 256         |   7.813 μs |   8.650 μs | 0.4741 μs |  0.15 |    0.01 | 0.0458 |      - |      - |    1551 B |        0.76 |
|             |             |            |            |           |       |         |        |        |        |           |             |
| **MQTTnet**     | **1024**        |  **55.151 μs** |  **42.822 μs** | **2.3472 μs** |  **1.00** |    **0.05** |      **-** |      **-** |      **-** |    **3590 B** |        **1.00** |
| Mqtt.Client | 1024        |   8.940 μs |   9.839 μs | 0.5393 μs |  0.16 |    0.01 | 0.0916 |      - |      - |    2995 B |        0.83 |
|             |             |            |            |           |       |         |        |        |        |           |             |
| **MQTTnet**     | **4096**        |  **63.285 μs** |  **44.940 μs** | **2.4633 μs** |  **1.00** |    **0.05** | **0.2441** |      **-** |      **-** |    **9721 B** |        **1.00** |
| Mqtt.Client | 4096        |  30.228 μs |  18.588 μs | 1.0188 μs |  0.48 |    0.02 | 0.2747 | 0.0916 | 0.0305 |   11160 B |        1.15 |
|             |             |            |            |           |       |         |        |        |        |           |             |
| **MQTTnet**     | **16384**       |  **63.687 μs** | **162.832 μs** | **8.9254 μs** |  **1.01** |    **0.17** | **1.0986** |      **-** |      **-** |   **34371 B** |        **1.00** |
| Mqtt.Client | 16384       |  60.577 μs |  14.596 μs | 0.8000 μs |  0.96 |    0.11 | 0.9766 |      - |      - |   34247 B |        1.00 |
|             |             |            |            |           |       |         |        |        |        |           |             |
| **MQTTnet**     | **65536**       | **166.459 μs** |  **94.782 μs** | **5.1953 μs** |  **1.00** |    **0.04** | **5.3711** | **0.9766** |      **-** |  **133338 B** |        **1.00** |
| Mqtt.Client | 65536       | 169.189 μs |  72.095 μs | 3.9518 μs |  1.02 |    0.03 | 4.6387 | 0.9766 |      - |  133439 B |        1.00 |

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
| **MQTTnet**     | **64**          | **146.9 μs** | **107.52 μs** |  **5.89 μs** |  **1.00** |    **0.05** |      **-** |      **-** |   **3.85 KB** |        **1.00** |
| Mqtt.Client | 64          | 138.6 μs | 138.98 μs |  7.62 μs |  0.94 |    0.06 |      - |      - |   2.47 KB |        0.64 |
|             |             |          |           |          |       |         |        |        |           |             |
| **MQTTnet**     | **256**         | **148.6 μs** |  **40.62 μs** |  **2.23 μs** |  **1.00** |    **0.02** |      **-** |      **-** |   **4.22 KB** |        **1.00** |
| Mqtt.Client | 256         | 130.4 μs | 172.27 μs |  9.44 μs |  0.88 |    0.06 |      - |      - |   2.84 KB |        0.67 |
|             |             |          |           |          |       |         |        |        |           |             |
| **MQTTnet**     | **1024**        | **146.2 μs** |  **75.88 μs** |  **4.16 μs** |  **1.00** |    **0.03** |      **-** |      **-** |   **5.72 KB** |        **1.00** |
| Mqtt.Client | 1024        | 129.9 μs | 119.23 μs |  6.54 μs |  0.89 |    0.04 |      - |      - |   4.34 KB |        0.76 |
|             |             |          |           |          |       |         |        |        |           |             |
| **MQTTnet**     | **4096**        | **159.3 μs** |  **69.39 μs** |  **3.80 μs** |  **1.00** |    **0.03** | **0.2441** |      **-** |  **11.72 KB** |        **1.00** |
| Mqtt.Client | 4096        | 136.1 μs |  79.12 μs |  4.34 μs |  0.85 |    0.03 | 0.2441 |      - |  10.41 KB |        0.89 |
|             |             |          |           |          |       |         |        |        |           |             |
| **MQTTnet**     | **16384**       | **172.4 μs** | **135.66 μs** |  **7.44 μs** |  **1.00** |    **0.05** | **0.9766** |      **-** |  **35.94 KB** |        **1.00** |
| Mqtt.Client | 16384       | 173.1 μs |  35.85 μs |  1.96 μs |  1.01 |    0.04 | 1.2207 |      - |  34.65 KB |        0.96 |
|             |             |          |           |          |       |         |        |        |           |             |
| **MQTTnet**     | **65536**       | **278.2 μs** |  **70.57 μs** |  **3.87 μs** |  **1.00** |    **0.02** | **4.8828** | **0.4883** | **132.82 KB** |        **1.00** |
| Mqtt.Client | 65536       | 299.2 μs | 195.63 μs | 10.72 μs |  1.08 |    0.04 | 4.3945 | 0.4883 | 131.63 KB |        0.99 |

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
| Method      | PayloadSize | Mean     | Error     | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------ |------------ |---------:|----------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| **MQTTnet**     | **64**          | **262.4 μs** | **108.53 μs** |  **5.95 μs** |  **1.00** |    **0.03** |      **-** |      **-** |   **6.72 KB** |        **1.00** |
| Mqtt.Client | 64          | 234.2 μs | 199.82 μs | 10.95 μs |  0.89 |    0.04 |      - |      - |   3.95 KB |        0.59 |
|             |             |          |           |          |       |         |        |        |           |             |
| **MQTTnet**     | **256**         | **264.8 μs** | **168.83 μs** |  **9.25 μs** |  **1.00** |    **0.04** |      **-** |      **-** |   **7.06 KB** |        **1.00** |
| Mqtt.Client | 256         | 241.0 μs | 362.69 μs | 19.88 μs |  0.91 |    0.07 |      - |      - |   4.33 KB |        0.61 |
|             |             |          |           |          |       |         |        |        |           |             |
| **MQTTnet**     | **1024**        | **285.5 μs** | **322.06 μs** | **17.65 μs** |  **1.00** |    **0.08** |      **-** |      **-** |   **8.57 KB** |        **1.00** |
| Mqtt.Client | 1024        | 228.2 μs | 125.05 μs |  6.85 μs |  0.80 |    0.05 |      - |      - |   5.83 KB |        0.68 |
|             |             |          |           |          |       |         |        |        |           |             |
| **MQTTnet**     | **4096**        | **275.8 μs** | **126.09 μs** |  **6.91 μs** |  **1.00** |    **0.03** | **0.4883** |      **-** |  **14.58 KB** |        **1.00** |
| Mqtt.Client | 4096        | 269.9 μs | 264.12 μs | 14.48 μs |  0.98 |    0.05 |      - |      - |   11.9 KB |        0.82 |
|             |             |          |           |          |       |         |        |        |           |             |
| **MQTTnet**     | **16384**       | **311.8 μs** |  **58.93 μs** |  **3.23 μs** |  **1.00** |    **0.01** | **0.9766** |      **-** |   **38.8 KB** |        **1.00** |
| Mqtt.Client | 16384       | 312.9 μs | 152.39 μs |  8.35 μs |  1.00 |    0.02 | 0.9766 |      - |  36.13 KB |        0.93 |
|             |             |          |           |          |       |         |        |        |           |             |
| **MQTTnet**     | **65536**       | **359.5 μs** | **192.07 μs** | **10.53 μs** |  **1.00** |    **0.04** | **4.8828** | **0.4883** | **135.65 KB** |        **1.00** |
| Mqtt.Client | 65536       | 456.0 μs | 479.47 μs | 26.28 μs |  1.27 |    0.07 | 4.8828 |      - | 133.14 KB |        0.98 |

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
| **&#39;MQTTnet receive&#39;**     | **64**          | **194.6 μs** |    **60.15 μs** |  **3.30 μs** |  **1.00** |    **0.02** |      **-** |      **-** |    **4.9 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 64          | 195.6 μs |   221.05 μs | 12.12 μs |  1.01 |    0.06 |      - |      - |   3.36 KB |        0.69 |
|                       |             |          |             |          |       |         |        |        |           |             |
| **&#39;MQTTnet receive&#39;**     | **256**         | **226.2 μs** |   **214.24 μs** | **11.74 μs** |  **1.00** |    **0.06** |      **-** |      **-** |   **5.61 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 256         | 170.0 μs |   185.19 μs | 10.15 μs |  0.75 |    0.05 |      - |      - |   3.86 KB |        0.69 |
|                       |             |          |             |          |       |         |        |        |           |             |
| **&#39;MQTTnet receive&#39;**     | **1024**        | **196.7 μs** |   **103.54 μs** |  **5.68 μs** |  **1.00** |    **0.04** | **0.2441** |      **-** |   **8.59 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 1024        | 188.8 μs |   150.67 μs |  8.26 μs |  0.96 |    0.04 |      - |      - |   6.12 KB |        0.71 |
|                       |             |          |             |          |       |         |        |        |           |             |
| **&#39;MQTTnet receive&#39;**     | **4096**        | **227.1 μs** |    **67.63 μs** |  **3.71 μs** |  **1.00** |    **0.02** | **0.4883** |      **-** |  **20.63 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 4096        | 198.3 μs |   257.90 μs | 14.14 μs |  0.87 |    0.06 | 0.4883 |      - |  15.16 KB |        0.74 |
|                       |             |          |             |          |       |         |        |        |           |             |
| **&#39;MQTTnet receive&#39;**     | **16384**       | **291.2 μs** | **1,199.69 μs** | **65.76 μs** |  **1.04** |    **0.31** | **2.4414** |      **-** |  **69.04 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 16384       | 206.5 μs |   153.63 μs |  8.42 μs |  0.74 |    0.17 | 1.7090 |      - |  51.36 KB |        0.74 |
|                       |             |          |             |          |       |         |        |        |           |             |
| **&#39;MQTTnet receive&#39;**     | **65536**       | **681.0 μs** | **1,311.25 μs** | **71.87 μs** |  **1.01** |    **0.13** | **8.7891** | **1.9531** | **262.87 KB** |        **1.00** |
| &#39;Mqtt.Client receive&#39; | 65536       | 320.5 μs |   773.35 μs | 42.39 μs |  0.47 |    0.07 | 7.3242 | 0.9766 | 196.22 KB |        0.75 |

