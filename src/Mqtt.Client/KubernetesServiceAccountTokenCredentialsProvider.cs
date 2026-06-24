// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Mqtt.Client;

/// <summary>
/// Reads a Kubernetes service-account token (SAT) from a mounted file and presents it as the MQTT
/// password. The token is re-read on every (re)connect, and the file is watched for rotation: when
/// the token changes on disk, <see cref="CredentialsChanged"/> fires and the client reconnects to
/// present the fresh token.
/// <para>
/// Kubernetes refreshes a projected/bound service-account token well before it expires (replacing
/// the file via an atomic symlink swap). Change detection combines periodic polling — the reliable
/// baseline — with a <see cref="FileSystemWatcher"/> for faster response.
/// </para>
/// </summary>
public sealed class KubernetesServiceAccountTokenCredentialsProvider
    : IMqttCredentialsProvider, IMqttCredentialsChangeNotifier, IDisposable
{
    /// <summary>
    /// Default in-cluster mount path of the projected service-account token.
    /// </summary>
    public const string DefaultTokenPath =
        "/var/run/secrets/kubernetes.io/serviceaccount/token";

    private readonly string _tokenPath;
    private readonly string? _username;
    private readonly TimeSpan _pollInterval;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cts = new();

    private byte[] _lastToken = Array.Empty<byte>();
    private FileSystemWatcher? _watcher;
    private Task? _pollLoop;
    private int _started;
    private int _disposed;

    /// <inheritdoc />
    public event EventHandler? CredentialsChanged;

    /// <summary>
    /// Creates a provider that reads the token from <paramref name="tokenPath"/>.
    /// </summary>
    /// <param name="tokenPath">
    /// Token file path; defaults to <see cref="DefaultTokenPath"/>.
    /// </param>
    /// <param name="username">Optional fixed MQTT username; the token is always the password.</param>
    /// <param name="pollInterval">
    /// How often to re-check the file for rotation; defaults to 5 minutes. Must be positive.
    /// </param>
    public KubernetesServiceAccountTokenCredentialsProvider(
        string? tokenPath = null,
        string? username = null,
        TimeSpan? pollInterval = null)
    {
        _tokenPath = string.IsNullOrEmpty(tokenPath) ? DefaultTokenPath : tokenPath!;
        _username = username;
        _pollInterval = pollInterval ?? TimeSpan.FromMinutes(5);
        if (_pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollInterval), "Poll interval must be positive.");
        }
    }

    /// <inheritdoc />
    public ValueTask<MqttCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
    {
        var token = ReadToken();
        lock (_gate)
        {
            _lastToken = token;
        }
        EnsureWatching();
        return new ValueTask<MqttCredentials>(new MqttCredentials(_username, token));
    }

    private byte[] ReadToken()
    {
        // Open with FileShare.ReadWrite | FileShare.Delete so a concurrent writer (the kubelet
        // replacing the token via an atomic rename, or a test rewriting/deleting the file) is never
        // locked out; small file, read to the end.
        using var fs = new FileStream(
            _tokenPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        return TrimTrailingWhitespace(ms.ToArray());
    }

    // A service-account token is a base64url-encoded JWT (no embedded whitespace), so trimming
    // trailing ASCII whitespace/newlines only strips a possible terminator, never token data.
    private static byte[] TrimTrailingWhitespace(byte[] bytes)
    {
        var end = bytes.Length;
        while (end > 0)
        {
            var b = bytes[end - 1];
            if (b == (byte)' ' || b == (byte)'\t' || b == (byte)'\n' || b == (byte)'\r')
            {
                end--;
            }
            else
            {
                break;
            }
        }
        if (end == bytes.Length) return bytes;
        var trimmed = new byte[end];
        Array.Copy(bytes, trimmed, end);
        return trimmed;
    }

    private void EnsureWatching()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        _pollLoop = Task.Run(() => PollLoopAsync(_cts.Token));
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(_tokenPath));
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                var watcher = new FileSystemWatcher(dir!)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
                        | NotifyFilters.CreationTime | NotifyFilters.DirectoryName,
                    IncludeSubdirectories = false,
                };
                watcher.Changed += OnFileSystemEvent;
                watcher.Created += OnFileSystemEvent;
                watcher.Renamed += OnFileSystemEvent;
                watcher.Deleted += OnFileSystemEvent;
                watcher.EnableRaisingEvents = true;
                _watcher = watcher;
            }
        }
        catch
        {
            // FileSystemWatcher is best-effort (it may be unavailable or miss symlink swaps); the
            // poll loop is the reliable baseline.
        }
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e) => CheckForChange();

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pollInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            CheckForChange();
        }
    }

    private void CheckForChange()
    {
        byte[] current;
        try
        {
            current = ReadToken();
        }
        catch (Exception)
        {
            // Transient read failure (e.g. mid symlink swap, or the file briefly absent); the next
            // poll/event will pick up the change.
            return;
        }

        bool changed;
        lock (_gate)
        {
            changed = !BytesEqual(current, _lastToken);
            if (changed)
            {
                _lastToken = current;
            }
        }
        if (changed)
        {
            CredentialsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool BytesEqual(byte[] a, byte[] b)
        => ((ReadOnlySpan<byte>)a).SequenceEqual(b);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts.Cancel();
        try
        {
            _watcher?.Dispose();
        }
        catch
        {
            // Best-effort teardown.
        }
        _cts.Dispose();
    }
}
