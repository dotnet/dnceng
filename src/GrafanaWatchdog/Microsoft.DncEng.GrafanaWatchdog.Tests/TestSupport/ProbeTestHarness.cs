// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Net.Http;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.DncEng.GrafanaWatchdog.Configuration;
using Microsoft.DncEng.GrafanaWatchdog.Probing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.DncEng.GrafanaWatchdog.Tests.TestSupport;

/// <summary>
/// Wires up a <see cref="GrafanaWatchdogProbe"/> with fakes for its collaborators (HTTP, token
/// credential, telemetry), so tests only need to configure the scenario-specific behavior.
/// </summary>
internal sealed class ProbeTestHarness : IDisposable
{
    private readonly HttpClient _httpClient;

    public ProbeTestHarness(FakeTokenCredential credential, GrafanaWatchdogOptions options, ScriptedHttpMessageHandler? handler = null)
    {
        Handler = handler ?? new ScriptedHttpMessageHandler();
        Credential = credential;
        Channel = new FakeTelemetryChannel();

        var telemetryConfiguration = new TelemetryConfiguration
        {
            TelemetryChannel = Channel,
            ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000",
        };
        TelemetryClient = new TelemetryClient(telemetryConfiguration);

        _httpClient = new HttpClient(Handler);
        Probe = new GrafanaWatchdogProbe(
            _httpClient,
            Credential,
            Options.Create(options),
            TelemetryClient,
            NullLogger<GrafanaWatchdogProbe>.Instance);
    }

    public ScriptedHttpMessageHandler Handler { get; }

    public FakeTokenCredential Credential { get; }

    public FakeTelemetryChannel Channel { get; }

    public TelemetryClient TelemetryClient { get; }

    public GrafanaWatchdogProbe Probe { get; }

    public void Dispose()
    {
        _httpClient.Dispose();
        TelemetryClient.Flush();
    }
}
