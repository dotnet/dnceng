// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DncEng.GrafanaWatchdog.Probing;
using Microsoft.Extensions.Logging;

namespace Microsoft.DncEng.GrafanaWatchdog;

/// <summary>
/// Timer-triggered entry point that runs one Grafana watchdog cycle every 5 minutes.
/// </summary>
public sealed class GrafanaWatchdogTimerFunction
{
    private readonly IGrafanaWatchdogProbe _probe;
    private readonly ILogger<GrafanaWatchdogTimerFunction> _logger;

    public GrafanaWatchdogTimerFunction(IGrafanaWatchdogProbe probe, ILogger<GrafanaWatchdogTimerFunction> logger)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("GrafanaWatchdog")]
    public async Task RunAsync([TimerTrigger("0 */5 * * * *")] TimerInfo timerInfo, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Grafana watchdog cycle. IsPastDue: {IsPastDue}", timerInfo.IsPastDue);

        WatchdogCycleResult result = await _probe.RunCycleAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Completed Grafana watchdog cycle. Workspaces: {WorkspaceCount}, FailedWorkspaces: {FailedWorkspaces}",
            result.WorkspaceResults.Count,
            result.FailedWorkspaceCount);
    }
}
