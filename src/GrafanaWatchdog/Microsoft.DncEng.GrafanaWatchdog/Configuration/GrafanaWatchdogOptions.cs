// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

namespace Microsoft.DncEng.GrafanaWatchdog.Configuration;

/// <summary>
/// Options controlling the Grafana watchdog probe cycle. Bound from the "GrafanaWatchdog" configuration
/// section, which the Function App populates via app settings (e.g. "GrafanaWatchdog__Workspaces__0__Name").
/// </summary>
public sealed class GrafanaWatchdogOptions
{
    /// <summary>
    /// The configuration section name this type is bound from.
    /// </summary>
    public const string SectionName = "GrafanaWatchdog";

    /// <summary>
    /// The Grafana workspaces probed on every cycle. A failure in one workspace does not prevent the
    /// others from being probed.
    /// </summary>
    public List<GrafanaWorkspaceOptions> Workspaces { get; set; } = new();

    /// <summary>
    /// Number of additional attempts made after an initial transient failure for a single HTTP probe.
    /// A value of 1 (the default) means each probe is attempted at most twice.
    /// </summary>
    public int RetryCount { get; set; } = 1;

    /// <summary>
    /// Per-HTTP-request timeout applied independently to each attempt of each probe.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);
}
