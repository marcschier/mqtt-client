// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Reflection;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
namespace Mqtt.Client.Benchmarks;

public static class Program
{
    public static int Main(string[] args)
    {
        var argList = new List<string>(args);
        var full = argList.Remove("--full");
        var report = argList.Remove("--report");

        IConfig config = new BenchConfig(full);

        var summaries = BenchmarkSwitcher
            .FromAssembly(Assembly.GetExecutingAssembly())
            .Run(argList.ToArray(), config);

        if (report)
        {
            SummaryGenerator.Generate();
        }
        return summaries.Any(s => s.HasCriticalValidationErrors) ? 1 : 0;
    }
}
