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
| MQTTnet     |  78.23 ns | 150.14 ns | 8.230 ns |  1.01 |    0.13 |      - |      - |      - |         - |          NA |
| Mqtt.Client | 115.29 ns | 103.33 ns | 5.664 ns |  1.49 |    0.16 | 0.0038 | 0.0002 | 0.0002 |         - |          NA |
