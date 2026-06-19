# Samples

## Username + password auth

```csharp
var client = MqttClient.CreateBuilder()
    .ConnectTo("mqtt://broker")
    .WithCredentials("user", "password")
    .Build();
```

## Mutual TLS

```csharp
using var cert = new X509Certificate2("client.pfx", "p@ss");

var client = MqttClient.CreateBuilder()
    .ConnectTo("mqtts://broker:8883")
    .WithClientCertificate(cert)
    .Build();
```

## Custom TLS chain validation

```csharp
var client = MqttClient.CreateBuilder()
    .ConnectTo("mqtts://broker:8883")
    .WithTls(tls =>
    {
        tls.RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
            certificate?.GetCertHashString() == ExpectedFingerprint;
    })
    .Build();
```

## Connect through a SOCKS5 proxy

Route the broker connection through a SOCKS5 proxy (RFC 1928). Supported for the TCP and TLS
transports. The broker host is resolved at the proxy by default (remote DNS).

```csharp
var client = MqttClient.CreateBuilder()
    .ConnectTo("mqtt://broker:1883")
    .WithSocks5Proxy("proxy.internal", 1080)
    .Build();
```

With username/password authentication (RFC 1929), and combined with TLS to the broker — TLS still
terminates at the broker through the tunnel, so certificate validation targets the broker host:

```csharp
var client = MqttClient.CreateBuilder()
    .ConnectTo("mqtts://broker:8883")
    .WithSocks5Proxy("proxy.internal", 1080, username: "u", password: "p")
    .Build();
```

To resolve the broker host locally instead of at the proxy, configure the options directly:

```csharp
var client = MqttClient.CreateBuilder()
    .ConnectTo("mqtt://broker:1883")
    .WithSocks5Proxy(new Socks5ProxyOptions
    {
        Host = "proxy.internal",
        Port = 1080,
        ResolveHostnamesRemotely = false,
    })
    .Build();
```

## Last will

```csharp
var client = MqttClient.CreateBuilder()
    .ConnectTo("mqtt://broker")
    .WithClientId("svc-1")
    .WithLastWill(new MqttLastWill
    {
        Topic = "svc-1/status",
        Payload = "offline"u8.ToArray(),
        QoS = MqttQoS.AtLeastOnce,
        Retain = true,
    })
    .Build();
```

## Dependency injection + hosted reconnect

```csharp
services
    .AddMqttClient(o =>
    {
        o.Host = "broker";
        o.Port = 1883;
        o.ClientId = "svc-1";
    })
    .AddMqttClientHostedReconnect();
```

`MqttClient` is registered as a singleton and the hosted service connects on `Start`
and disconnects on `Stop`. Inject `MqttClient` into your background services.

## Subscribe with backpressure tuning

```csharp
await using var sub = await client.SubscribeAsync("telemetry/#", new MqttSubscriptionOptions
{
    QoS = MqttQoS.AtLeastOnce,
    Capacity = 8192,
    Overflow = MqttOverflowMode.DropOldest, // newest-is-best
    NoLocal = true,                          // MQTT 5: don't loop our own publishes back
});

await foreach (var msg in sub.Reader.ReadAllAsync())
{
    Process(msg.Payload.Span);
}
```

## Fire-and-forget burst

```csharp
foreach (var sample in samples)
{
    if (!client.TryPublish($"telemetry/{sample.Id}", sample.PayloadSpan))
    {
        // outbound queue full — apply your own backpressure
        await Task.Yield();
    }
}
```

## MQTT 5 user properties + correlation

```csharp
await client.PublishAsync(
    "rpc/request",
    payload,
    MqttQoS.AtLeastOnce,
    properties: new MqttPublishProperties
    {
        ResponseTopic = "rpc/response",
        CorrelationData = correlationId,
        UserProperties = new[]
        {
            new MqttUserProperty("x-trace-id", traceId),
        },
    });
```

More: see [advanced topics](./advanced.md) for custom transports, persistence stores,
and AOT publishing.
