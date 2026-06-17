// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Diagnostics;

namespace Mqtt.Client.Diagnostics;

internal static class MqttActivitySource
{
    public const string Name = "Mqtt.Client";
    public static readonly ActivitySource Source = new(Name);
}
