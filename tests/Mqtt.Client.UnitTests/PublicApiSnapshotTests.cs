// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;

namespace Mqtt.Client.UnitTests;

/// <summary>
/// Public API snapshot guard. Any change to the public surface of <c>Mqtt.Client</c> must be
/// reflected in <c>tests/Mqtt.Client.UnitTests/PublicApi.expected.txt</c>. This is the
/// minimum guardrail to keep semver-major changes intentional once v1.0 ships.
/// </summary>
public class PublicApiSnapshotTests
{
    [Test]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only reflection over the library assembly.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Test-only reflection over the library assembly.")]
    public async Task Public_surface_matches_snapshot()
    {
        var actual = DumpPublicApi(typeof(MqttClient).Assembly);
        var expectedPath = Path.Combine(AppContext.BaseDirectory, "PublicApi.expected.txt");
        if (!File.Exists(expectedPath))
        {
            File.WriteAllText(expectedPath, actual);
        }
        var expected = File.ReadAllText(expectedPath).Replace("\r\n", "\n");
        var actualNormalized = actual.Replace("\r\n", "\n");
        await Assert.That(actualNormalized).IsEqualTo(expected);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only reflection over the library assembly.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Test-only reflection over the library assembly.")]
    private static string DumpPublicApi(Assembly asm)
    {
        var sb = new StringBuilder();
        var types = asm.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal);
        foreach (var t in types)
        {
            sb.Append(t.IsInterface ? "interface " : t.IsEnum ? "enum " : t.IsValueType ? "struct " : "class ");
            sb.AppendLine(t.FullName);
            foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .OrderBy(x => x.ToString(), StringComparer.Ordinal))
            {
                sb.Append("  ");
                sb.AppendLine(m.ToString());
            }
        }
        return sb.ToString();
    }
}
