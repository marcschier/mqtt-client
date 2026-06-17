// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using Mqtt.Client.FuzzTests.Harnesses;
using SharpFuzz;

namespace Mqtt.Client.FuzzTests;

/// <summary>
/// libFuzzer entry point. The first command-line argument selects the harness:
///   <c>decoder</c> | <c>codec-roundtrip</c> | <c>topic-trie</c>
/// Selection can also be made via the <c>FUZZ_HARNESS</c> environment variable.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--seed-corpus")
        {
            CorpusGenerator.GenerateAll(args[1]);
            Console.WriteLine($"Seeded corpus at {args[1]}");
            return 0;
        }

        var name = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("FUZZ_HARNESS") ?? "decoder";
        ReadOnlySpanAction action = name.ToLowerInvariant() switch
        {
            "decoder" => DecoderHarness.Run,
            "codec-roundtrip" => CodecRoundtripHarness.Run,
            "topic-trie" => TopicTrieHarness.Run,
            _ => throw new ArgumentException($"Unknown harness: {name}"),
        };
        Fuzzer.LibFuzzer.Run(action);
        return 0;
    }
}
