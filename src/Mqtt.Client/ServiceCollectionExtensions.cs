// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mqtt.Client.Persistence;

namespace Mqtt.Client.DependencyInjection;

/// <summary>Registers <see cref="MqttClient"/> with <see cref="IServiceCollection"/>.</summary>
public static class MqttClientServiceCollectionExtensions
{
    /// <summary>Registers a singleton <see cref="MqttClient"/> bound to the configured options.</summary>
    public static IServiceCollection AddMqttClient(this IServiceCollection services, Action<MqttClientOptions> configure)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        services.AddOptions<MqttClientOptions>().Configure(configure).ValidateOnStart();
        services.AddSingleton<MqttClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<MqttClientOptions>>().Value;
            var loggerFactory = sp.GetService<ILoggerFactory>();
            var store = sp.GetService<IPersistentSessionStore>();
            return new MqttClient(opts, loggerFactory, store);
        });
        return services;
    }

    /// <summary>
    /// Registers a named <see cref="MqttClient"/> so multiple clients can coexist in the same
    /// container. Resolve via <see cref="IMqttClientFactory.Create"/> from your service.
    /// </summary>
    public static IServiceCollection AddMqttClient(this IServiceCollection services, string name, Action<MqttClientOptions> configure)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("Name is required.", nameof(name));
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        services.AddOptions<MqttClientOptions>(name).Configure(configure).ValidateOnStart();
        services.TryAddSingleton<IMqttClientFactory, MqttClientFactory>();
        return services;
    }

    /// <summary>Adds a hosted service that connects on Start and disconnects on Stop, with auto-reconnect honored.</summary>
    public static IServiceCollection AddMqttClientHostedReconnect(this IServiceCollection services)
    {
        services.AddHostedService<MqttClientHostedService>();
        return services;
    }
}

/// <summary>Resolves named <see cref="MqttClient"/> instances registered via the named DI overload.</summary>
public interface IMqttClientFactory
{
    /// <summary>Returns (creating on first access) the client for <paramref name="name"/>.</summary>
    MqttClient Create(string name);
}

internal sealed class MqttClientFactory : IMqttClientFactory, IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, MqttClient> _clients = new(StringComparer.Ordinal);

    public MqttClientFactory(IServiceProvider services) { _services = services; }

    public MqttClient Create(string name)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("Name is required.", nameof(name));
        return _clients.GetOrAdd(name, n =>
        {
            var opts = _services.GetRequiredService<IOptionsMonitor<MqttClientOptions>>().Get(n);
            var loggerFactory = _services.GetService<ILoggerFactory>();
            var store = _services.GetService<IPersistentSessionStore>();
            return new MqttClient(opts, loggerFactory, store);
        });
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var c in _clients.Values)
        {
            try { await c.DisposeAsync().ConfigureAwait(false); } catch { }
        }
        _clients.Clear();
    }
}

internal sealed class MqttClientHostedService : IHostedService
{
    private readonly MqttClient _client;
    private readonly ILogger<MqttClientHostedService> _logger;

    public MqttClientHostedService(MqttClient client, ILogger<MqttClientHostedService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken) => _client.ConnectAsync(cancellationToken).ContinueWith(t =>
    {
        if (t.IsFaulted)
        {
            _logger.LogError(t.Exception, "MQTT initial connect failed.");
        }
    }, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _client.DisconnectAsync(cancellationToken).ConfigureAwait(false);
    }
}
