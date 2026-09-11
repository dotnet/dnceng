// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;

namespace Microsoft.DncEng.GrafanaWatchdog.Models;

/// <summary>
/// The subset of the Grafana "/api/health" response body that the watchdog validates.
/// </summary>
internal sealed class GrafanaHealthResponse
{
    [JsonPropertyName("database")]
    public string? Database { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}
