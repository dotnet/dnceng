// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using AwesomeAssertions;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.DncEng.GrafanaWatchdog.Configuration;
using Microsoft.DncEng.GrafanaWatchdog.Probing;
using Microsoft.DncEng.GrafanaWatchdog.Tests.TestSupport;
using NUnit.Framework;

namespace Microsoft.DncEng.GrafanaWatchdog.Tests;

[TestFixture]
public class GrafanaWatchdogProbeTests
{
    private const string WorkspaceAEndpoint = "https://workspace-a.example.com";
    private const string WorkspaceBEndpoint = "https://workspace-b.example.com";

    private static readonly string HealthyHealthBody = """{"database":"ok","version":"11.2.0"}""";
    private static readonly string HealthyOrgBody = """{"id":1,"name":"Main Org."}""";

    private static GrafanaWorkspaceOptions Workspace(string name, string endpoint) => new() { Name = name, Endpoint = endpoint };

    private static GrafanaWatchdogOptions OptionsFor(params GrafanaWorkspaceOptions[] workspaces) => new()
    {
        Workspaces = workspaces.ToList(),
        RetryCount = 1,
        RequestTimeout = TimeSpan.FromSeconds(5),
    };

    private static void EnqueueHealthyResponses(ScriptedHttpMessageHandler handler, string endpoint)
    {
        handler.Enqueue($"{endpoint}/api/health", ScriptedHttpMessageHandler.Respond(HttpStatusCode.OK, HealthyHealthBody));
        handler.Enqueue($"{endpoint}/api/org", ScriptedHttpMessageHandler.Respond(HttpStatusCode.OK, HealthyOrgBody));
    }

    [Test]
    public async Task ProbeWorkspaceAsync_RequestsTokenForGrafanaDashboardScope()
    {
        var workspace = Workspace("dnceng-grafana", WorkspaceAEndpoint);
        var credential = new FakeTokenCredential("fake-token");
        using var harness = new ProbeTestHarness(credential, OptionsFor(workspace));
        EnqueueHealthyResponses(harness.Handler, WorkspaceAEndpoint);

        await harness.Probe.ProbeWorkspaceAsync(workspace, CancellationToken.None);

        credential.LastRequestedScopes.Should().NotBeNull();
        credential.LastRequestedScopes.Should().Contain(GrafanaWatchdogProbe.TokenScope);
        GrafanaWatchdogProbe.TokenScope.Should().Be("https://dashboard.azure.com/.default");
    }

    [Test]
    public async Task ProbeWorkspaceAsync_CallsBothEndpoints_WithBearerAuthorization()
    {
        var workspace = Workspace("dnceng-grafana", WorkspaceAEndpoint);
        var credential = new FakeTokenCredential("fake-token");
        using var harness = new ProbeTestHarness(credential, OptionsFor(workspace));
        EnqueueHealthyResponses(harness.Handler, WorkspaceAEndpoint);

        WorkspaceProbeResult result = await harness.Probe.ProbeWorkspaceAsync(workspace, CancellationToken.None);

        result.AllSucceeded.Should().BeTrue();
        harness.Handler.Requests.Should().HaveCount(2);
        harness.Handler.Requests.Select(r => r.RequestUri!.ToString()).Should().BeEquivalentTo(
            $"{WorkspaceAEndpoint}/api/health", $"{WorkspaceAEndpoint}/api/org");
        harness.Handler.Requests.Should().OnlyContain(r => r.Headers.Authorization!.Scheme == "Bearer" && r.Headers.Authorization.Parameter == "fake-token");
    }

    [Test]
    public async Task ProbeWorkspaceAsync_MarksHealthUnhealthy_WhenDatabaseStatusIsNotOk()
    {
        var workspace = Workspace("dnceng-grafana", WorkspaceAEndpoint);
        var credential = new FakeTokenCredential("fake-token");
        using var harness = new ProbeTestHarness(credential, OptionsFor(workspace));
        harness.Handler.Enqueue($"{WorkspaceAEndpoint}/api/health", ScriptedHttpMessageHandler.Respond(HttpStatusCode.OK, """{"database":"failing"}"""));
        harness.Handler.Enqueue($"{WorkspaceAEndpoint}/api/org", ScriptedHttpMessageHandler.Respond(HttpStatusCode.OK, HealthyOrgBody));

        WorkspaceProbeResult result = await harness.Probe.ProbeWorkspaceAsync(workspace, CancellationToken.None);

        EndpointProbeResult health = result.EndpointResults.Single(r => r.EndpointName == "health");
        health.Success.Should().BeFalse();
        health.Message.Should().Contain("failing");
        result.EndpointResults.Single(r => r.EndpointName == "org").Success.Should().BeTrue();

    }

    [Test]
    public async Task ProbeWorkspaceAsync_MarksOrgUnhealthy_WhenBodyIsMissingId()
    {
        var workspace = Workspace("dnceng-grafana", WorkspaceAEndpoint);
        var credential = new FakeTokenCredential("fake-token");
        using var harness = new ProbeTestHarness(credential, OptionsFor(workspace));
        harness.Handler.Enqueue($"{WorkspaceAEndpoint}/api/health", ScriptedHttpMessageHandler.Respond(HttpStatusCode.OK, HealthyHealthBody));
        harness.Handler.Enqueue($"{WorkspaceAEndpoint}/api/org", ScriptedHttpMessageHandler.Respond(HttpStatusCode.OK, "{}"));

        WorkspaceProbeResult result = await harness.Probe.ProbeWorkspaceAsync(workspace, CancellationToken.None);

        EndpointProbeResult org = result.EndpointResults.Single(r => r.EndpointName == "org");
        org.Success.Should().BeFalse();
        org.Message.Should().Contain("id");
    }

    [Test]
    public async Task ProbeWorkspaceAsync_RetriesOnce_OnTransientStatusCode_ThenSucceeds()
    {
        var workspace = Workspace("dnceng-grafana", WorkspaceAEndpoint);
        var credential = new FakeTokenCredential("fake-token");
        using var harness = new ProbeTestHarness(credential, OptionsFor(workspace));
        harness.Handler.Enqueue($"{WorkspaceAEndpoint}/api/health", ScriptedHttpMessageHandler.Respond(HttpStatusCode.ServiceUnavailable));
        harness.Handler.Enqueue($"{WorkspaceAEndpoint}/api/health", ScriptedHttpMessageHandler.Respond(HttpStatusCode.OK, HealthyHealthBody));
        harness.Handler.Enqueue($"{WorkspaceAEndpoint}/api/org", ScriptedHttpMessageHandler.Respond(HttpStatusCode.OK, HealthyOrgBody));

        WorkspaceProbeResult result = await harness.Probe.ProbeWorkspaceAsync(workspace, CancellationToken.None);

        EndpointProbeResult health = result.EndpointResults.Single(r => r.EndpointName == "health");
        health.Success.Should().BeTrue();
        health.AttemptCount.Should().Be(2);
        harness.Handler.Requests.Count(r => r.RequestUri!.ToString() == $"{WorkspaceAEndpoint}/api/health").Should().Be(2);
    }

    [Test]
    public async Task ProbeWorkspaceAsync_GivesUpAfterConfiguredRetries_OnRepeatedTransientFailures()
    {
        var workspace = Workspace("dnceng-grafana", WorkspaceAEndpoint);
        var credential = new FakeTokenCredential("fake-token");
        using var harness = new ProbeTestHarness(credential, OptionsFor(workspace)); // RetryCount = 1 -> max 2 attempts
        harness.Handler.Enqueue($"{WorkspaceAEndpoint}/api/health", ScriptedHttpMessageHandler.Respond(HttpStatusCode.ServiceUnavailable));
        harness.Handler.Enqueue($"{WorkspaceAEndpoint}/api/health", ScriptedHttpMessageHandler.Respond(HttpStatusCode.ServiceUnavailable));
        harness.Handler.Enqueue($"{WorkspaceAEndpoint}/api/org", ScriptedHttpMessageHandler.Respond(HttpStatusCode.OK, HealthyOrgBody));

        WorkspaceProbeResult result = await harness.Probe.ProbeWorkspaceAsync(workspace, CancellationToken.None);

        EndpointProbeResult health = result.EndpointResults.Single(r => r.EndpointName == "health");
        health.Success.Should().BeFalse();
        health.AttemptCount.Should().Be(2);
        health.Message.Should().Contain("503");
        harness.Handler.Requests.Count(r => r.RequestUri!.ToString() == $"{WorkspaceAEndpoint}/api/health").Should().Be(2);
    }

    [Test]
    public async Task ProbeWorkspaceAsync_DoesNotRetry_OnNonTransientStatusCode()
    {
        var workspace = Workspace("dnceng-grafana", WorkspaceAEndpoint);
        var credential = new FakeTokenCredential("fake-token");
        using var harness = new ProbeTestHarness(credential, OptionsFor(workspace));
        harness.Handler.Enqueue($"{WorkspaceAEndpoint}/api/health", ScriptedHttpMessageHandler.Respond(HttpStatusCode.NotFound));
        harness.Handler.Enqueue($"{WorkspaceAEndpoint}/api/org", ScriptedHttpMessageHandler.Respond(HttpStatusCode.OK, HealthyOrgBody));

        WorkspaceProbeResult result = await harness.Probe.ProbeWorkspaceAsync(workspace, CancellationToken.None);

        EndpointProbeResult health = result.EndpointResults.Single(r => r.EndpointName == "health");
        health.Success.Should().BeFalse();
        health.AttemptCount.Should().Be(1);
        harness.Handler.Requests.Count(r => r.RequestUri!.ToString() == $"{WorkspaceAEndpoint}/api/health").Should().Be(1);
    }

    [Test]
    public async Task ProbeWorkspaceAsync_RetriesOnTransportException_ThenSucceeds()
    {
        var workspace = Workspace("dnceng-grafana", WorkspaceAEndpoint);
        var credential = new FakeTokenCredential("fake-token");
        using var harness = new ProbeTestHarness(credential, OptionsFor(workspace));
        harness.Handler.Enqueue($"{WorkspaceAEndpoint}/api/health", ScriptedHttpMessageHandler.Throw(new HttpRequestException("connection reset")));
        harness.Handler.Enqueue($"{WorkspaceAEndpoint}/api/health", ScriptedHttpMessageHandler.Respond(HttpStatusCode.OK, HealthyHealthBody));
        harness.Handler.Enqueue($"{WorkspaceAEndpoint}/api/org", ScriptedHttpMessageHandler.Respond(HttpStatusCode.OK, HealthyOrgBody));

        WorkspaceProbeResult result = await harness.Probe.ProbeWorkspaceAsync(workspace, CancellationToken.None);

        EndpointProbeResult health = result.EndpointResults.Single(r => r.EndpointName == "health");
        health.Success.Should().BeTrue();
        health.AttemptCount.Should().Be(2);
    }

    [Test]
    public async Task ProbeWorkspaceAsync_RetriesOnPerRequestTimeout_ThenSucceeds()
    {
        var workspace = Workspace("dnceng-grafana", WorkspaceAEndpoint);
        var credential = new FakeTokenCredential("fake-token");
        var options = OptionsFor(workspace);
        options.RequestTimeout = TimeSpan.FromMilliseconds(200);
        using var harness = new ProbeTestHarness(credential, options);
        harness.Handler.Enqueue(
            $"{WorkspaceAEndpoint}/api/health",
            ScriptedHttpMessageHandler.RespondAfterDelay(TimeSpan.FromSeconds(5), HttpStatusCode.OK, HealthyHealthBody));
        harness.Handler.Enqueue($"{WorkspaceAEndpoint}/api/health", ScriptedHttpMessageHandler.Respond(HttpStatusCode.OK, HealthyHealthBody));
        harness.Handler.Enqueue($"{WorkspaceAEndpoint}/api/org", ScriptedHttpMessageHandler.Respond(HttpStatusCode.OK, HealthyOrgBody));

        WorkspaceProbeResult result = await harness.Probe.ProbeWorkspaceAsync(workspace, CancellationToken.None);

        EndpointProbeResult health = result.EndpointResults.Single(r => r.EndpointName == "health");
        health.Success.Should().BeTrue();
        health.AttemptCount.Should().Be(2);
    }

    [Test]
    public async Task ProbeWorkspaceAsync_MarksWorkspaceUnhealthy_WhenTokenAcquisitionFails_WithoutCallingGrafana()
    {
        var workspace = Workspace("dnceng-grafana", WorkspaceAEndpoint);
        var credential = new FakeTokenCredential(_ => throw new AuthenticationFailedException("managed identity unavailable"));
        using var harness = new ProbeTestHarness(credential, OptionsFor(workspace));

        WorkspaceProbeResult result = await harness.Probe.ProbeWorkspaceAsync(workspace, CancellationToken.None);

        result.EndpointResults.Should().ContainSingle();
        result.EndpointResults[0].Success.Should().BeFalse();
        result.EndpointResults[0].Message.Should().Contain("Token acquisition failed");
        harness.Handler.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task RunCycleAsync_ContinuesProbingOtherWorkspaces_AfterOneWorkspaceCannotAuthenticate()
    {
        var workspaceA = Workspace("workspace-a", WorkspaceAEndpoint);
        var workspaceB = Workspace("workspace-b", WorkspaceBEndpoint);
        int tokenRequests = 0;
        var credential = new FakeTokenCredential(_ =>
        {
            tokenRequests++;
            return tokenRequests == 1
                ? throw new AuthenticationFailedException("managed identity unavailable")
                : new AccessToken("fake-token", DateTimeOffset.UtcNow.AddHours(1));
        });
        using var harness = new ProbeTestHarness(credential, OptionsFor(workspaceA, workspaceB));

        EnqueueHealthyResponses(harness.Handler, WorkspaceBEndpoint);

        WatchdogCycleResult cycleResult = await harness.Probe.RunCycleAsync(CancellationToken.None);

        cycleResult.WorkspaceResults.Should().HaveCount(2);
        cycleResult.FailedWorkspaceCount.Should().Be(1);

        WorkspaceProbeResult resultA = cycleResult.WorkspaceResults.Single(r => r.WorkspaceName == "workspace-a");
        resultA.EndpointResults.Should().ContainSingle(r => r.EndpointName == "token" && !r.Success);

        WorkspaceProbeResult resultB = cycleResult.WorkspaceResults.Single(r => r.WorkspaceName == "workspace-b");
        resultB.AllSucceeded.Should().BeTrue();

        var availability = harness.Channel.SentItems.OfType<AvailabilityTelemetry>().ToList();
        availability.Should().HaveCount(2);
        availability.Should().OnlyContain(item => item.Name == GrafanaWatchdogProbe.AvailabilityTestName);
        availability.Single(item => item.Properties["WorkspaceName"] == "workspace-a").Success.Should().BeFalse();
        availability.Single(item => item.Properties["WorkspaceName"] == "workspace-b").Success.Should().BeTrue();

        harness.Channel.SentItems.OfType<EventTelemetry>().Count(e => e.Name == GrafanaWatchdogProbe.HeartbeatEventName).Should().Be(1);
    }

    [Test]
    public async Task RunCycleAsync_DoesNotEmitHeartbeat_WhenAnUnexpectedExceptionAbortsTheCycle()
    {
        var workspace = Workspace("workspace-a", WorkspaceAEndpoint);
        var credential = new FakeTokenCredential("fake-token");
        using var harness = new ProbeTestHarness(credential, OptionsFor(workspace));
        harness.Handler.Enqueue(
            $"{WorkspaceAEndpoint}/api/health",
            ScriptedHttpMessageHandler.Throw(new InvalidOperationException("unexpected bug")));

        Func<Task> act = () => harness.Probe.RunCycleAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("unexpected bug");
        harness.Channel.SentItems.OfType<EventTelemetry>().Should()
            .NotContain(item => item.Name == GrafanaWatchdogProbe.HeartbeatEventName);
    }

    [Test]
    public async Task RunCycleAsync_EmitsHeartbeat_WhenAllWorkspacesAreHealthy()
    {
        var workspaceA = Workspace("workspace-a", WorkspaceAEndpoint);
        var credential = new FakeTokenCredential("fake-token");
        using var harness = new ProbeTestHarness(credential, OptionsFor(workspaceA));
        EnqueueHealthyResponses(harness.Handler, WorkspaceAEndpoint);

        WatchdogCycleResult cycleResult = await harness.Probe.RunCycleAsync(CancellationToken.None);

        cycleResult.FailedWorkspaceCount.Should().Be(0);
        EventTelemetry heartbeat = harness.Channel.SentItems.OfType<EventTelemetry>().Single(e => e.Name == GrafanaWatchdogProbe.HeartbeatEventName);
        heartbeat.Properties["FailedWorkspaces"].Should().Be("0");
        heartbeat.Properties["WorkspacesProbed"].Should().Be("1");
        AvailabilityTelemetry availability = harness.Channel.SentItems.OfType<AvailabilityTelemetry>().Single();
        availability.Name.Should().Be(GrafanaWatchdogProbe.AvailabilityTestName);
        availability.Properties["WorkspaceName"].Should().Be("workspace-a");
        availability.Properties["EndpointResults"].Should().Contain("health:True").And.Contain("org:True");
    }

    [Test]
    public async Task RunCycleAsync_FailsInvocation_WhenTelemetryFlushFails()
    {
        var workspace = Workspace("workspace-a", WorkspaceAEndpoint);
        var credential = new FakeTokenCredential("fake-token");
        using var harness = new ProbeTestHarness(credential, OptionsFor(workspace));
        harness.Channel.FlushResult = false;
        EnqueueHealthyResponses(harness.Handler, WorkspaceAEndpoint);

        Func<Task> act = () => harness.Probe.RunCycleAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*did not flush*");
    }
}
