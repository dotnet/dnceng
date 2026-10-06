// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.DncEng.GrafanaWatchdog.Probing;

/// <summary>
/// The outcome of probing a single endpoint (e.g. "api/health") on a single Grafana workspace.
/// </summary>
/// <param name="EndpointName">A short name for the endpoint probed, e.g. "health" or "org".</param>
/// <param name="Success">Whether the endpoint responded with a healthy status and a valid body.</param>
/// <param name="Message">A human-readable summary of the result, surfaced in telemetry and logs.</param>
/// <param name="Duration">Total wall-clock time spent probing, including any retries.</param>
/// <param name="AttemptCount">How many HTTP attempts were made (1 unless a transient failure was retried).</param>
public sealed record EndpointProbeResult(string EndpointName, bool Success, string Message, TimeSpan Duration, int AttemptCount);

/// <summary>
/// The outcome of probing every configured endpoint on a single Grafana workspace.
/// </summary>
public sealed record WorkspaceProbeResult(string WorkspaceName, IReadOnlyList<EndpointProbeResult> EndpointResults)
{
    /// <summary>
    /// True only if every probed endpoint on this workspace succeeded.
    /// </summary>
    public bool AllSucceeded => EndpointResults.Count > 0 && EndpointResults.All(result => result.Success);
}

/// <summary>
/// The outcome of a single watchdog cycle across every configured workspace.
/// </summary>
public sealed record WatchdogCycleResult(IReadOnlyList<WorkspaceProbeResult> WorkspaceResults)
{
    /// <summary>
    /// Count of workspaces whose authenticated probe did not fully succeed.
    /// </summary>
    public int FailedWorkspaceCount => WorkspaceResults.Count(workspace => !workspace.AllSucceeded);
}
