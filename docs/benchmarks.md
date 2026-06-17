# Mqtt.Client vs MQTTnet — benchmark results

_Generated 2026-06-17 10:52 UTC._

Run with:
```
dotnet run -c Release --project tests/Mqtt.Client.Benchmarks -- --filter '*' --report
dotnet run -c Release --project tests/Mqtt.Client.Benchmarks -- --filter '*' --full --report
```

## Mqtt.Client.Benchmarks.Codec.DecodePublishBenchmark-report-github

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
| **MQTTnet**     | **64**          |    **111.7 ns** |    **200.81 ns** |    **11.01 ns** |  **1.01** |    **0.12** | **0.0081** |      **-** |      **-** |     **248 B** |        **1.00** |
| Mqtt.Client | 64          |    205.5 ns |    122.38 ns |     6.71 ns |  1.85 |    0.16 | 0.0083 |      - |      - |     192 B |        0.77 |
|             |             |             |              |             |       |         |        |        |        |           |             |
| **MQTTnet**     | **256**         |    **152.4 ns** |     **92.81 ns** |     **5.09 ns** |  **1.00** |    **0.04** | **0.0145** |      **-** |      **-** |     **440 B** |        **1.00** |
| Mqtt.Client | 256         |    241.7 ns |    650.90 ns |    35.68 ns |  1.59 |    0.21 | 0.0126 |      - |      - |     384 B |        0.87 |
|             |             |             |              |             |       |         |        |        |        |           |             |
| **MQTTnet**     | **1024**        |    **432.1 ns** |    **764.85 ns** |    **41.92 ns** |  **1.01** |    **0.12** | **0.0401** |      **-** |      **-** |    **1208 B** |        **1.00** |
| Mqtt.Client | 1024        |    488.4 ns |    600.57 ns |    32.92 ns |  1.14 |    0.12 | 0.0381 |      - |      - |    1152 B |        0.95 |
|             |             |             |              |             |       |         |        |        |        |           |             |
| **MQTTnet**     | **4096**        |  **1,178.8 ns** |  **1,861.91 ns** |   **102.06 ns** |  **1.01** |    **0.11** | **0.1431** |      **-** |      **-** |    **4280 B** |        **1.00** |
| Mqtt.Client | 4096        |  1,135.9 ns |    700.93 ns |    38.42 ns |  0.97 |    0.08 | 0.1392 |      - |      - |    4224 B |        0.99 |
|             |             |             |              |             |       |         |        |        |        |           |             |
| **MQTTnet**     | **16384**       |  **4,140.3 ns** | **12,923.76 ns** |   **708.39 ns** |  **1.02** |    **0.21** | **0.7515** | **0.0076** | **0.0076** |         **-** |          **NA** |
| Mqtt.Client | 16384       |  4,617.7 ns |  4,287.97 ns |   235.04 ns |  1.14 |    0.16 | 0.7629 | 0.0076 | 0.0076 |         - |          NA |
|             |             |             |              |             |       |         |        |        |        |           |             |
| **MQTTnet**     | **65536**       |  **9,960.1 ns** | **22,788.94 ns** | **1,249.14 ns** |  **1.01** |    **0.15** | **2.5482** |      **-** |      **-** |   **65720 B** |        **1.00** |
| Mqtt.Client | 65536       | 12,931.6 ns | 12,390.91 ns |   679.19 ns |  1.31 |    0.15 | 2.7008 |      - |      - |   65664 B |        1.00 |

## Mqtt.Client.Benchmarks.Codec.EncodePublishBenchmark-report-github

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]   : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method      | PayloadSize | Mean        | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Gen2   | Allocated | Alloc Ratio |
|------------ |------------ |------------:|----------:|----------:|------:|--------:|-------:|-------:|-------:|----------:|------------:|
| **MQTTnet**     | **64**          |    **38.17 ns** |  **62.40 ns** |  **3.420 ns** |  **1.01** |    **0.11** |      **-** |      **-** |      **-** |         **-** |          **NA** |
| Mqtt.Client | 64          |   101.28 ns | 111.59 ns |  6.116 ns |  2.67 |    0.24 | 0.0030 | 0.0001 | 0.0001 |         - |          NA |
|             |             |             |           |           |       |         |        |        |        |           |             |
| **MQTTnet**     | **256**         |    **44.44 ns** |  **30.80 ns** |  **1.689 ns** |  **1.00** |    **0.05** |      **-** |      **-** |      **-** |         **-** |          **NA** |
| Mqtt.Client | 256         |   129.42 ns |  68.31 ns |  3.744 ns |  2.91 |    0.12 | 0.0019 |      - |      - |      64 B |          NA |
|             |             |             |           |           |       |         |        |        |        |           |             |
| **MQTTnet**     | **1024**        |    **38.26 ns** |  **20.78 ns** |  **1.139 ns** |  **1.00** |    **0.04** |      **-** |      **-** |      **-** |         **-** |          **NA** |
| Mqtt.Client | 1024        |   124.19 ns | 187.73 ns | 10.290 ns |  3.25 |    0.25 | 0.0019 |      - |      - |      64 B |          NA |
|             |             |             |           |           |       |         |        |        |        |           |             |
| **MQTTnet**     | **4096**        |    **55.68 ns** |  **26.35 ns** |  **1.444 ns** |  **1.00** |    **0.03** |      **-** |      **-** |      **-** |         **-** |          **NA** |
| Mqtt.Client | 4096        |   246.39 ns | 297.85 ns | 16.326 ns |  4.43 |    0.27 | 0.0019 |      - |      - |      64 B |          NA |
|             |             |             |           |           |       |         |        |        |        |           |             |
| **MQTTnet**     | **16384**       |    **50.81 ns** |  **61.51 ns** |  **3.371 ns** |  **1.00** |    **0.08** |      **-** |      **-** |      **-** |         **-** |          **NA** |
| Mqtt.Client | 16384       |   453.98 ns | 113.25 ns |  6.208 ns |  8.96 |    0.51 | 0.0024 |      - |      - |      64 B |          NA |
|             |             |             |           |           |       |         |        |        |        |           |             |
| **MQTTnet**     | **65536**       |    **40.88 ns** |  **19.25 ns** |  **1.055 ns** |  **1.00** |    **0.03** |      **-** |      **-** |      **-** |         **-** |          **NA** |
| Mqtt.Client | 65536       | 2,446.68 ns | 422.94 ns | 23.183 ns | 59.88 |    1.44 |      - |      - |      - |      64 B |          NA |

## Mqtt.Client.Benchmarks.Codec.EncodeSubscribeBenchmark-report-github

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]   : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method      | Mean      | Error     | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Gen2   | Allocated | Alloc Ratio |
|------------ |----------:|----------:|---------:|------:|--------:|-------:|-------:|-------:|----------:|------------:|
| MQTTnet     |  60.84 ns |  29.96 ns | 1.642 ns |  1.00 |    0.03 |      - |      - |      - |         - |          NA |
| Mqtt.Client | 134.80 ns | 180.55 ns | 9.896 ns |  2.22 |    0.15 | 0.0037 | 0.0001 | 0.0001 |         - |          NA |

## Mqtt.Client.Benchmarks.EndToEnd.ConnectLatencyBenchmark-report-github

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
| MQTTnet     | 4.771 ms | 5.709 ms | 0.3129 ms |  1.00 |    0.08 |  37.43 KB |        1.00 |
| Mqtt.Client | 4.976 ms | 6.358 ms | 0.3485 ms |  1.05 |    0.09 |  43.58 KB |        1.16 |

## Mqtt.Client.Benchmarks.EndToEnd.PublishQoS0Benchmark-report-github

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8655)
Intel Xeon W-2235 CPU 3.80GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.301
  [Host]   : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method      | PayloadSize | Mean       | Error       | StdDev     | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------ |------------ |-----------:|------------:|-----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **MQTTnet**     | **64**          |  **41.930 μs** |  **10.8534 μs** |  **0.5949 μs** |  **1.00** |    **0.02** |      **-** |      **-** |   **1.62 KB** |        **1.00** |
| Mqtt.Client | 64          |   5.268 μs |   0.5262 μs |  0.0288 μs |  0.13 |    0.00 | 0.0305 |      - |   1.15 KB |        0.71 |
|             |             |            |             |            |       |         |        |        |           |             |
| **MQTTnet**     | **256**         |  **40.933 μs** |  **42.4965 μs** |  **2.3294 μs** |  **1.00** |    **0.07** | **0.0610** |      **-** |   **1.92 KB** |        **1.00** |
| Mqtt.Client | 256         |   7.820 μs |   1.8393 μs |  0.1008 μs |  0.19 |    0.01 | 0.0458 |      - |   1.51 KB |        0.79 |
|             |             |            |             |            |       |         |        |        |           |             |
| **MQTTnet**     | **1024**        |  **39.098 μs** |  **38.4819 μs** |  **2.1093 μs** |  **1.00** |    **0.07** | **0.0610** |      **-** |   **3.41 KB** |        **1.00** |
| Mqtt.Client | 1024        |   9.450 μs |   3.7933 μs |  0.2079 μs |  0.24 |    0.01 | 0.0916 |      - |   3.01 KB |        0.88 |
|             |             |            |             |            |       |         |        |        |           |             |
| **MQTTnet**     | **4096**        |  **45.915 μs** |  **63.5310 μs** |  **3.4824 μs** |  **1.00** |    **0.09** | **0.3052** |      **-** |   **9.42 KB** |        **1.00** |
| Mqtt.Client | 4096        |  18.174 μs |  15.6451 μs |  0.8576 μs |  0.40 |    0.03 | 0.1526 |      - |    9.1 KB |        0.97 |
|             |             |            |             |            |       |         |        |        |           |             |
| **MQTTnet**     | **16384**       |  **47.869 μs** |  **21.3808 μs** |  **1.1720 μs** |  **1.00** |    **0.03** | **1.1597** |      **-** |  **33.48 KB** |        **1.00** |
| Mqtt.Client | 16384       |  40.218 μs |   7.6866 μs |  0.4213 μs |  0.84 |    0.02 | 1.1597 | 0.0610 |  33.27 KB |        0.99 |
|             |             |            |             |            |       |         |        |        |           |             |
| **MQTTnet**     | **65536**       | **184.461 μs** | **289.4180 μs** | **15.8640 μs** |  **1.01** |    **0.11** | **5.3711** | **0.4883** | **130.25 KB** |        **1.00** |
| Mqtt.Client | 65536       | 218.085 μs | 945.0573 μs | 51.8018 μs |  1.19 |    0.26 | 5.6152 | 1.2207 | 130.19 KB |        1.00 |

