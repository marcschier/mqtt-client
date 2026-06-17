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
