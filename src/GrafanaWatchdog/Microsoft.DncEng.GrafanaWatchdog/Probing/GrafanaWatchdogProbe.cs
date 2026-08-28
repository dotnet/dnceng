// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.DncEng.GrafanaWatchdog.Configuration;
using Microsoft.DncEng.GrafanaWatchdog.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.DncEng.GrafanaWatchdog.Probing;

/// <summary>
/// Probes Azure Managed Grafana workspaces using the Function's managed identity and records the
/// results as Application Insights availability telemetry, plus a heartbeat event per completed cycle.
/// </summary>
public sealed class GrafanaWatchdogProbe : IGrafanaWatchdogProbe
{
    /// <summary>
    /// Scope requested when acquiring a token for Azure Managed Grafana's data-plane API.
    /// See https://learn.microsoft.com/azure/managed-grafana/ for background: Azure Managed Grafana
    /// validates Entra ID tokens issued for this resource, not a workspace-specific audience.
    /// </summary>
    public const string TokenScope = "https://dashboard.azure.com/.default";

    /// <summary>
    /// Name of the heartbeat event emitted once per completed cycle, regardless of individual
    /// probe outcomes. Used by the "missing heartbeat" Azure Monitor alert to detect a watchdog
    /// that has stopped running entirely (as opposed to one that is running but seeing failures).
    /// </summary>
    public const string HeartbeatEventName = "GrafanaWatchdogHeartbeat";

    public const string AvailabilityTestName = "GrafanaWorkspaceProbe";

    private const string HealthEndpointName = "health";
    private const string OrgEndpointName = "org";
    private const string TokenEndpointName = "token";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private readonly IOptions<GrafanaWatchdogOptions> _options;
    private readonly TelemetryClient _telemetryClient;
    private readonly ILogger<GrafanaWatchdogProbe> _logger;

    public GrafanaWatchdogProbe(
        HttpClient httpClient,
        TokenCredential credential,
        IOptions<GrafanaWatchdogOptions> options,
        TelemetryClient telemetryClient,
        ILogger<GrafanaWatchdogProbe> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _credential = credential ?? throw new ArgumentNullException(nameof(credential));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<WatchdogCycleResult> RunCycleAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<GrafanaWorkspaceOptions> workspaces = _options.Value.Workspaces;
        var workspaceResults = new List<WorkspaceProbeResult>(workspaces.Count);

        foreach (GrafanaWorkspaceOptions workspace in workspaces)
        {
            WorkspaceProbeResult result = await ProbeWorkspaceAsync(workspace, cancellationToken).ConfigureAwait(false);
            RecordAvailability(workspace, result);
            workspaceResults.Add(result);
        }

        var cycleResult = new WatchdogCycleResult(workspaceResults);
        RecordHeartbeat(cycleResult);

        if (!await _telemetryClient.FlushAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Application Insights did not flush the Grafana watchdog telemetry.");
        }

        return cycleResult;
    }

    /// <summary>
    /// Probes both the "api/health" and "api/org" endpoints of a single workspace using one access
    /// token. Internal (rather than private) so unit tests
    /// can exercise a single workspace directly.
    /// </summary>
    internal async Task<WorkspaceProbeResult> ProbeWorkspaceAsync(GrafanaWorkspaceOptions workspace, CancellationToken cancellationToken)
    {
        AccessToken token;
        try
        {
            token = await _credential
                .GetTokenAsync(new TokenRequestContext(new[] { TokenScope }), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AuthenticationFailedException ex)
        {
            _logger.LogError(ex, "Failed to acquire an access token for Grafana workspace {WorkspaceName}", workspace.Name);
            var failure = new EndpointProbeResult(TokenEndpointName, false, $"Token acquisition failed: {ex.Message}", TimeSpan.Zero, 0);
            return new WorkspaceProbeResult(workspace.Name, new[] { failure });
        }

        var endpointResults = new List<EndpointProbeResult>(2);

        EndpointProbeResult healthResult = await ProbeEndpointAsync(
            workspace, HealthEndpointName, "api/health", token.Token, ValidateHealthBody, cancellationToken).ConfigureAwait(false);
        endpointResults.Add(healthResult);

        EndpointProbeResult orgResult = await ProbeEndpointAsync(
            workspace, OrgEndpointName, "api/org", token.Token, ValidateOrgBody, cancellationToken).ConfigureAwait(false);
        endpointResults.Add(orgResult);

        return new WorkspaceProbeResult(workspace.Name, endpointResults);
    }

    /// <summary>
    /// Calls a single endpoint with a bounded retry: transient failures (408/429/5xx status codes,
    /// transport exceptions, and per-request timeouts) are retried up to <see cref="GrafanaWatchdogOptions.RetryCount"/>
    /// additional times. Non-transient failures (e.g. 404, or a response body that fails validation)
    /// return immediately without retrying.
    /// </summary>
    private async Task<EndpointProbeResult> ProbeEndpointAsync(
        GrafanaWorkspaceOptions workspace,
        string endpointName,
        string relativePath,
        string accessToken,
        Func<string, (bool IsValid, string Message)> validateBody,
        CancellationToken cancellationToken)
    {
        int maxAttempts = Math.Max(1, _options.Value.RetryCount + 1);
        var stopwatch = Stopwatch.StartNew();
        string lastFailureMessage = "Probe did not run";

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.Value.RequestTimeout);

            bool isTransient = false;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(workspace.Endpoint), relativePath));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                using HttpResponseMessage response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                    .ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

                if (IsTransientStatusCode(response.StatusCode))
                {
                    lastFailureMessage = $"Received transient status code {(int)response.StatusCode} ({response.StatusCode})";
                    isTransient = true;
                }
                else if (!response.IsSuccessStatusCode)
                {
                    return new EndpointProbeResult(endpointName, false, $"Received status code {(int)response.StatusCode} ({response.StatusCode})", stopwatch.Elapsed, attempt);
                }
                else
                {
                    (bool isValid, string message) = validateBody(body);
                    return new EndpointProbeResult(endpointName, isValid, message, stopwatch.Elapsed, attempt);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The overall cancellationToken was not the cause, so this must be our per-request
                // timeout (timeoutCts) firing - treat it as a transient, retryable failure.
                lastFailureMessage = $"Request timed out after {_options.Value.RequestTimeout}";
                isTransient = true;
            }
            catch (HttpRequestException ex)
            {
                lastFailureMessage = $"Transport failure: {ex.Message}";
                isTransient = true;
            }

            if (isTransient && attempt < maxAttempts)
            {
                _logger.LogWarning(
                    "Retrying Grafana probe {WorkspaceName}/{EndpointName} (attempt {Attempt} of {MaxAttempts}): {Reason}",
                    workspace.Name, endpointName, attempt, maxAttempts, lastFailureMessage);
            }
        }

        return new EndpointProbeResult(endpointName, false, lastFailureMessage, stopwatch.Elapsed, maxAttempts);
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
    {
        int code = (int)statusCode;
        return statusCode == HttpStatusCode.RequestTimeout
            || statusCode == HttpStatusCode.TooManyRequests
            || code >= 500;
    }

    private static (bool IsValid, string Message) ValidateHealthBody(string body)
    {
        GrafanaHealthResponse? health;
        try
        {
            health = JsonSerializer.Deserialize<GrafanaHealthResponse>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            return (false, $"Health response body could not be parsed: {ex.Message}");
        }

        if (health is null || string.IsNullOrEmpty(health.Database))
        {
            return (false, "Health response was missing a 'database' field");
        }

        if (!string.Equals(health.Database, "ok", StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"Health response reported database status '{health.Database}'");
        }

        return (true, $"database=ok, version={health.Version ?? "unknown"}");
    }

    private static (bool IsValid, string Message) ValidateOrgBody(string body)
    {
        GrafanaOrgResponse? org;
        try
        {
            org = JsonSerializer.Deserialize<GrafanaOrgResponse>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            return (false, $"Org response body could not be parsed: {ex.Message}");
        }

        if (org is null || org.Id <= 0)
        {
            return (false, "Org response was missing a valid 'id' field");
        }

        return (true, $"orgId={org.Id}, name={org.Name ?? "unknown"}");
    }

    private void RecordAvailability(GrafanaWorkspaceOptions workspace, WorkspaceProbeResult result)
    {
        TimeSpan duration = TimeSpan.FromTicks(result.EndpointResults.Sum(endpoint => endpoint.Duration.Ticks));
        string message = string.Join(
            "; ",
            result.EndpointResults.Select(endpoint => $"{endpoint.EndpointName}: {endpoint.Message}"));

        var telemetry = new AvailabilityTelemetry
        {
            Name = AvailabilityTestName,
            RunLocation = "GrafanaWatchdog",
            Success = result.AllSucceeded,
            Message = message,
            Duration = duration,
            Timestamp = DateTimeOffset.UtcNow,
        };
        telemetry.Properties["WorkspaceName"] = workspace.Name;
        telemetry.Properties["EndpointResults"] = string.Join(
            ",",
            result.EndpointResults.Select(endpoint => $"{endpoint.EndpointName}:{endpoint.Success}"));
        telemetry.Properties["AttemptCount"] = result.EndpointResults
            .Sum(endpoint => endpoint.AttemptCount)
            .ToString(CultureInfo.InvariantCulture);

        _telemetryClient.TrackAvailability(telemetry);
    }

    private void RecordHeartbeat(WatchdogCycleResult cycleResult)
    {
        var properties = new Dictionary<string, string>
        {
            ["WorkspacesProbed"] = cycleResult.WorkspaceResults.Count.ToString(CultureInfo.InvariantCulture),
            ["FailedWorkspaces"] = cycleResult.FailedWorkspaceCount.ToString(CultureInfo.InvariantCulture),
        };

        _telemetryClient.TrackEvent(HeartbeatEventName, properties);
    }
}
