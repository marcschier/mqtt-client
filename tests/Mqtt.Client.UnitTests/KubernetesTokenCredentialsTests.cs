// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.IO;
using System.Text;

namespace Mqtt.Client.UnitTests;

public class KubernetesTokenCredentialsTests
{
    private static string NewTempTokenPath()
        => Path.Combine(Path.GetTempPath(), $"sat-{Guid.NewGuid():N}.token");

    private static void TryDeleteTemp(string path)
    {
        try { File.Delete(path); } catch (IOException) { /* best-effort temp cleanup */ }
    }

    [Test]
    [Timeout(10_000)]
    public async Task Reads_token_and_trims_trailing_newline(CancellationToken ct)
    {
        var path = NewTempTokenPath();
        await File.WriteAllTextAsync(path, "header.payload.signature\n", ct);
        try
        {
            using var provider = new KubernetesServiceAccountTokenCredentialsProvider(path, "svc");
            var creds = await provider.GetCredentialsAsync(ct);
            await Assert.That(creds.Username).IsEqualTo("svc");
            await Assert.That(Encoding.UTF8.GetString(creds.Password!))
                .IsEqualTo("header.payload.signature");
        }
        finally
        {
            TryDeleteTemp(path);
        }
    }

    [Test]
    [Timeout(10_000)]
    public async Task Raises_CredentialsChanged_when_token_file_changes(CancellationToken ct)
    {
        var path = NewTempTokenPath();
        await File.WriteAllTextAsync(path, "token-1", ct);
        try
        {
            using var provider = new KubernetesServiceAccountTokenCredentialsProvider(
                path, pollInterval: TimeSpan.FromMilliseconds(100));
            var changed = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            provider.CredentialsChanged += (_, _) => changed.TrySetResult();

            // The first read starts watching and sets the baseline.
            var first = await provider.GetCredentialsAsync(ct);
            await Assert.That(Encoding.UTF8.GetString(first.Password!)).IsEqualTo("token-1");

            // Rotate the token; the poll loop (and FileSystemWatcher) must detect it.
            await File.WriteAllTextAsync(path, "token-2", ct);
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);

            var second = await provider.GetCredentialsAsync(ct);
            await Assert.That(Encoding.UTF8.GetString(second.Password!)).IsEqualTo("token-2");
        }
        finally
        {
            TryDeleteTemp(path);
        }
    }

    [Test]
    [Timeout(10_000)]
    public async Task Dispose_stops_watching(CancellationToken ct)
    {
        var path = NewTempTokenPath();
        await File.WriteAllTextAsync(path, "token-1", ct);
        try
        {
            var provider = new KubernetesServiceAccountTokenCredentialsProvider(
                path, pollInterval: TimeSpan.FromMilliseconds(100));
            await provider.GetCredentialsAsync(ct);   // start watching
            var fired = false;
            provider.CredentialsChanged += (_, _) => fired = true;
            provider.Dispose();

            await File.WriteAllTextAsync(path, "token-2", ct);
            await Task.Delay(TimeSpan.FromMilliseconds(600), ct);
            await Assert.That(fired).IsFalse();
        }
        finally
        {
            TryDeleteTemp(path);
        }
    }

    [Test]
    public async Task Rejects_non_positive_poll_interval()
    {
        await Assert.That(() =>
        {
            _ = new KubernetesServiceAccountTokenCredentialsProvider(
                "/tmp/x", pollInterval: TimeSpan.Zero);
        }).Throws<ArgumentOutOfRangeException>();
    }

    // A credentials provider that can signal a change on demand, for testing the client wiring.
    private sealed class ManualNotifierProvider
        : IMqttCredentialsProvider, IMqttCredentialsChangeNotifier
    {
        private int _calls;

        public event EventHandler? CredentialsChanged;

        public ValueTask<MqttCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref _calls);
            return new ValueTask<MqttCredentials>(MqttCredentials.From($"user{n}", $"token{n}"));
        }

        public void Raise() => CredentialsChanged?.Invoke(this, EventArgs.Empty);
    }

    [Test]
    [Timeout(10_000)]
    public async Task CredentialsChange_reconnects_with_new_credentials(CancellationToken ct)
    {
        var factory = new MultiConnectFakeFactory();
        var provider = new ManualNotifierProvider();
        var client = new MqttClient(new MqttClientOptions
        {
            Host = "fake",
            ClientId = "c",
            ProtocolVersion = MqttProtocolVersion.V500,
            Reconnect = MqttReconnectPolicy.Fixed(TimeSpan.FromMilliseconds(50)),
            CredentialsProvider = provider,
        }, factory);
        await using var _0 = client;

        var connectTask = client.ConnectAsync(ct);
        var t1 = await factory.Created.ReadAsync(ct);
        var broker1 = new FakeBroker(t1);
        var connect1 = await broker1.ReadConnectAsync(ct);
        await broker1.SendConnAckAsync(ct: ct);
        await connectTask;
        await Assert.That(connect1.Username).IsEqualTo("user1");

        // Signal a credential change. The manual disconnect flag is set synchronously inside the
        // reconnect, so completing the broker side now lets the graceful teardown finish promptly
        // without racing the auto-reconnect path.
        provider.Raise();
        t1.ToClient.Complete();

        var t2 = await factory.Created.ReadAsync(ct);
        var broker2 = new FakeBroker(t2);
        var connect2 = await broker2.ReadConnectAsync(ct);
        await broker2.SendConnAckAsync(ct: ct);

        await Assert.That(connect2.Username).IsEqualTo("user2");
        await Assert.That(Encoding.UTF8.GetString(connect2.Password!)).IsEqualTo("token2");
    }

    [Test]
    [Timeout(10_000)]
    public async Task ReconnectAsync_forces_a_fresh_connect(CancellationToken ct)
    {
        var factory = new MultiConnectFakeFactory();
        var provider = new ManualNotifierProvider();
        var client = new MqttClient(new MqttClientOptions
        {
            Host = "fake",
            ClientId = "c",
            ProtocolVersion = MqttProtocolVersion.V500,
            Reconnect = MqttReconnectPolicy.Fixed(TimeSpan.FromMilliseconds(50)),
            CredentialsProvider = provider,
        }, factory);
        await using var _0 = client;

        var connectTask = client.ConnectAsync(ct);
        var t1 = await factory.Created.ReadAsync(ct);
        var broker1 = new FakeBroker(t1);
        await broker1.ReadConnectAsync(ct);
        await broker1.SendConnAckAsync(ct: ct);
        await connectTask;

        var reconnectTask = client.ReconnectAsync(ct);
        t1.ToClient.Complete();   // let the graceful teardown complete promptly

        var t2 = await factory.Created.ReadAsync(ct);
        var broker2 = new FakeBroker(t2);
        var connect2 = await broker2.ReadConnectAsync(ct);
        await broker2.SendConnAckAsync(ct: ct);
        await reconnectTask;

        await Assert.That(connect2.Username).IsEqualTo("user2");
        await Assert.That(client.State).IsEqualTo(MqttConnectionState.Connected);
    }

    [Test]
    [Timeout(5_000)]
    public async Task ReconnectAsync_throws_when_not_connected(CancellationToken ct)
    {
        var factory = new FakeTransportFactory();
        var client = new MqttClient(new MqttClientOptions
        {
            Host = "fake",
            ClientId = "c",
            Reconnect = null,
        }, factory);
        await using var _0 = client;
        await Assert.That(() => client.ReconnectAsync(ct)).Throws<InvalidOperationException>();
    }
}
