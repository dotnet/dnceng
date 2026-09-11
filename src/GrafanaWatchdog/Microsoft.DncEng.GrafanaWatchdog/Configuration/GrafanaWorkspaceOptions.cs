// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DncEng.GrafanaWatchdog.Configuration;

/// <summary>
/// Identifies a single Azure Managed Grafana workspace that the watchdog probes each cycle.
/// </summary>
public sealed class GrafanaWorkspaceOptions
{
    /// <summary>
    /// Friendly workspace name (e.g. "dnceng-grafana"). Used to tag telemetry and to group alerts
    /// per workspace; does not need to match the Azure resource name exactly, but should for clarity.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The workspace's HTTPS endpoint (e.g. "https://dnceng-grafana-xxxxxxxxxxxx.wus2.grafana.azure.com").
    /// Azure Managed Grafana endpoints include a randomly generated segment, so this must be supplied
    /// explicitly rather than derived from <see cref="Name"/>.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;
}
