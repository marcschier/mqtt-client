# Quickstart

Install the NuGet package:

```bash
dotnet add package Mqtt.Client
```

Connect, subscribe, publish:

```csharp
using Mqtt.Client;

await using var client = MqttClient.CreateBuilder()
    .ConnectTo("mqtt://broker.example.com:1883")
    .WithClientId("svc-1")
    .Build();

await client.ConnectAsync();

await using var sub = await client.SubscribeAsync("sensors/+/temp");

_ = Task.Run(async () =>
{
    await foreach (var msg in sub.Reader.ReadAllAsync())
    {
        Console.WriteLine($"{msg.Topic}: {msg.Payload.Length} bytes");
    }
});

await client.PublishAsync("commands/svc-1", "ping"u8.ToArray());
await client.DisconnectAsync();
```

That's it — three calls. The threading model is hidden, backpressure is built in,
and the API mirrors `System.Threading.Channels`.

## Endpoint URIs

| Scheme | Transport | Default port |
| --- | --- | --- |
| `mqtt://` | TCP | 1883 |
| `mqtts://` | TCP + TLS | 8883 |
| `ws://` | WebSocket | 80 |
| `wss://` | WebSocket + TLS | 443 |

## TLS

Plain `mqtts://` uses TLS 1.2 or 1.3 (whichever the OS supports), validates the broker
certificate against the system trust store, and enables CRL checking. Override via
`WithTls(...)` if you need mutual TLS or a custom trust store:

```csharp
var client = MqttClient.CreateBuilder()
    .ConnectTo("mqtts://broker:8883")
    .WithClientCertificate(myClientCert)
    .Build();
```

See [concepts](./concepts.md) for the bigger picture and
[samples](./samples.md) for runnable snippets.
