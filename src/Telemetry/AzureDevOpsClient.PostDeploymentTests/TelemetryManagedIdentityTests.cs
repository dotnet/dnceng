// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Internal.AzureDevOps.PostDeploymentTests;

/// <summary>
/// Post-deployment tests that validate bearer token (Entra ID) authentication
/// to Azure DevOps works correctly for the telemetry service.
///
/// In CI these run in the validateDeployment pipeline stage using an
/// <see cref="AzurePipelinesCredential"/> backed by the pipeline service
/// connection. Locally they fall back to <see cref="AzureCliCredential"/>
/// from an active <c>az login</c> session.
///
/// These tests exercise the same code path that the deployed service uses
/// with its Managed Identity — only the token source differs.
/// </summary>
[TestFixture]
[Category("PostDeployment")]
public class TelemetryManagedIdentityTests
{
    private TokenCredential _credential = null!;
    private ILogger<AzureDevOpsClient> _logger = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        _logger = LoggerFactory
            .Create(b => b.AddConsole())
            .CreateLogger<AzureDevOpsClient>();

        // Pipeline environment: use AzurePipelinesCredential from the service connection
        // (same pattern as SecretManager ScenarioTestsBase).
        string? clientId = Environment.GetEnvironmentVariable("AZURESUBSCRIPTION_CLIENT_ID");
        string? tenantId = Environment.GetEnvironmentVariable("AZURESUBSCRIPTION_TENANT_ID");
        string? serviceConnectionId = Environment.GetEnvironmentVariable("AZURESUBSCRIPTION_SERVICE_CONNECTION_ID");
        string? systemAccessToken = Environment.GetEnvironmentVariable("SYSTEM_ACCESSTOKEN");

        if (!string.IsNullOrEmpty(clientId)
            && !string.IsNullOrEmpty(tenantId)
            && !string.IsNullOrEmpty(serviceConnectionId)
            && !string.IsNullOrEmpty(systemAccessToken))
        {
            _credential = new AzurePipelinesCredential(tenantId, clientId, serviceConnectionId, systemAccessToken);
        }
        else
        {
            // Local dev: fall back to az login
            _credential = new AzureCliCredential();
        }
    }

    /// <summary>
    /// Validates that the Managed Identity can acquire a bearer token for
    /// Azure DevOps and successfully list builds from dnceng/internal.
    /// </summary>
    [Test]
    public async Task ManagedIdentity_CanListBuilds_FromDncengInternal()
    {
        var options = new AzureDevOpsClientOptions
        {
            Organization = "dnceng",
            UseManagedIdentity = true,
            MaxParallelRequests = 1,
        };

        var client = new AzureDevOpsClient(options, _logger, new SimpleHttpClientFactory(), _credential);

        var builds = await client.ListBuilds("internal", CancellationToken.None, limit: 3);

        Assert.That(builds, Is.Not.Null);
        Assert.That(builds.Length, Is.GreaterThan(0),
            "Expected at least one build from dnceng/internal using bearer token auth");

        TestContext.Out.WriteLine($"Retrieved {builds.Length} build(s) via Managed Identity:");
        foreach (var build in builds)
        {
            TestContext.Out.WriteLine($"  Build #{build.Id} — {build.Definition?.Name} — {build.Status}");
        }
    }

    /// <summary>
    /// Validates that the Managed Identity can read build timeline data,
    /// which is the core operation the telemetry service performs.
    /// </summary>
    [Test]
    public async Task ManagedIdentity_CanGetTimeline_FromDncengInternal()
    {
        var options = new AzureDevOpsClientOptions
        {
            Organization = "dnceng",
            UseManagedIdentity = true,
            MaxParallelRequests = 1,
        };

        var client = new AzureDevOpsClient(options, _logger, new SimpleHttpClientFactory(), _credential);

        // First get a recent build
        var builds = await client.ListBuilds("internal", CancellationToken.None, limit: 1);
        Assert.That(builds, Is.Not.Null);
        Assert.That(builds.Length, Is.GreaterThan(0), "Need at least one build to test timeline access");

        var build = builds[0];
        TestContext.Out.WriteLine($"Fetching timeline for build #{build.Id}...");

        var timeline = await client.GetTimelineAsync("internal", (int)build.Id, CancellationToken.None);

        Assert.That(timeline, Is.Not.Null, "Expected a non-null timeline from dnceng/internal build");
        TestContext.Out.WriteLine($"Timeline has {timeline!.Records?.Length ?? 0} record(s)");
    }

    /// <summary>
    /// Validates that the bearer token uses the correct Azure DevOps scope
    /// by requesting a token directly and inspecting it succeeds.
    /// </summary>
    [Test]
    public async Task ManagedIdentity_CanAcquireAzDoToken()
    {
        var context = new TokenRequestContext(
            new[] { AzureDevOpsClient.AzureDevOpsResourceId });

        var token = await _credential.GetTokenAsync(context, CancellationToken.None);

        Assert.That(token.Token, Is.Not.Null.And.Not.Empty,
            "Expected a non-empty bearer token for Azure DevOps");
        Assert.That(token.ExpiresOn, Is.GreaterThan(DateTimeOffset.UtcNow),
            "Token should not already be expired");

        TestContext.Out.WriteLine($"Token acquired (expires {token.ExpiresOn:u}, length={token.Token.Length})");
    }

    private sealed class SimpleHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }
}
