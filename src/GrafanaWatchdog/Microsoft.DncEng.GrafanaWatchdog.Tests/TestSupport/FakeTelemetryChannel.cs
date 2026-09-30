// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Channel;

namespace Microsoft.DncEng.GrafanaWatchdog.Tests.TestSupport;

/// <summary>
/// An <see cref="ITelemetryChannel"/> test double that captures every item sent through it instead of
/// transmitting to Application Insights, per Microsoft's documented approach for unit testing telemetry.
/// </summary>
internal sealed class FakeTelemetryChannel : ITelemetryChannel, IAsyncFlushable
{
    public List<ITelemetry> SentItems { get; } = new();

    public bool? DeveloperMode { get; set; }

    public string? EndpointAddress { get; set; }

    public bool FlushResult { get; set; } = true;

    public void Dispose()
    {
    }

    public void Flush()
    {
    }

    public Task<bool> FlushAsync(CancellationToken cancellationToken) => Task.FromResult(FlushResult);

    public void Send(ITelemetry item) => SentItems.Add(item);
}
