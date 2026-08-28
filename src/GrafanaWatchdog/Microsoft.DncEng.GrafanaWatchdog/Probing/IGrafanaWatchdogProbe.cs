// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DncEng.GrafanaWatchdog.Probing;

/// <summary>
/// Runs a single watchdog cycle: authenticates as the Function's managed identity, probes every
/// configured Grafana workspace, and emits Application Insights telemetry describing the result.
/// </summary>
public interface IGrafanaWatchdogProbe
{
    Task<WatchdogCycleResult> RunCycleAsync(CancellationToken cancellationToken);
}
