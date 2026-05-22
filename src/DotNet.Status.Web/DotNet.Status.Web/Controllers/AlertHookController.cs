// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Status.Web.Models;
using DotNet.Status.Web.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.DotNet.Internal.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNet.Status.Web.Controllers;

[ApiController]
[Route("api/alert")]
[AllowAnonymous]
public class AlertHookController : ControllerBase
{
    public const string NotificationTag = "Grafana Alert";
    public const string ActiveAlertTag = "Active Alert";
    public const string InactiveAlertTag = "Inactive Alert";
    public const string BodyLabelTextFormat = "Grafana-Automated-Alert-Id-{0}";
    public const string NotificationTagName = "NotificationId";

    private readonly IOptions<AzureDevOpsAlertOptions> _alertOptions;
    private readonly IOptions<GrafanaOptions> _grafanaOptions;
    private readonly ILogger _logger;
    private readonly IClientFactory<IAzureDevOpsClient> _azureDevOpsClientFactory;

    public AlertHookController(
        IClientFactory<IAzureDevOpsClient> azureDevOpsClientFactory,
        IOptions<AzureDevOpsAlertOptions> alertOptions,
        IOptions<GrafanaOptions> grafanaOptions,
        ILogger<AlertHookController> logger)
    {
        _azureDevOpsClientFactory = azureDevOpsClientFactory;
        _alertOptions = alertOptions;
        _grafanaOptions = grafanaOptions;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> NotifyAsync(GrafanaNotification notification)
    {
        if (!IsAuthorized())
        {
            _logger.LogWarning("Unauthorized alert webhook request received");
            return Unauthorized();
        }

        switch (notification.State)
        {
            case "ok":
                await CloseExistingNotificationAsync(notification);
                break;
            case "alerting":
            case "no_data":
                await OpenNewNotificationAsync(notification);
                break;
            default:
                return BadRequest();
        }

        return NoContent();
    }

    private Reference<IAzureDevOpsClient> GetAzureDevOpsClient()
    {
        return _azureDevOpsClientFactory.GetClient("dnceng");
    }

    private async Task OpenNewNotificationAsync(GrafanaNotification notification)
    {
        AzureDevOpsAlertOptions options = _alertOptions.Value;
        _logger.LogInformation(
            "Alert state detected for {ruleUrl} in stage {ruleState}, creating AzDO work item in {project}",
            notification.RuleUrl,
            notification.State,
            options.Project);

        using var clientRef = GetAzureDevOpsClient();
        var client = clientRef.Value;

        WorkItem existingWorkItem = await GetExistingWorkItemAsync(client, notification);

        if (existingWorkItem == null)
        {
            _logger.LogInformation("No existing work item found, creating new active work item with tag {tag}",
                ActiveAlertTag);

            var fields = new Dictionary<string, string>
            {
                ["System.Title"] = GenerateTitle(notification),
                ["System.Description"] = GenerateDescription(notification),
                ["System.State"] = "Backlog",
                ["System.Tags"] = BuildTagString(NotificationTag, ActiveAlertTag),
            };

            WorkItem workItem = await client.CreateWorkItemAsync(
                options.Project,
                options.WorkItemType,
                fields,
                options.AreaPath,
                CancellationToken.None);

            _logger.LogInformation("AzDO work item {project}/{workItemId} created",
                options.Project, workItem?.Id);
        }
        else
        {
            int workItemId = existingWorkItem.Id;
            _logger.LogInformation(
                "Found existing work item {workItemId}, updating tags and adding recurrence comment",
                workItemId);

            string currentTags = GetFieldValue(existingWorkItem, "System.Tags") ?? "";
            string newTags = UpdateTags(currentTags, addTag: ActiveAlertTag, removeTag: InactiveAlertTag);

            var fields = new Dictionary<string, string>
            {
                ["System.State"] = "Backlog",
                ["System.Tags"] = newTags,
            };

            await client.UpdateWorkItemAsync(options.Project, workItemId, fields, CancellationToken.None);
            await client.AddWorkItemCommentAsync(
                options.Project,
                workItemId,
                GenerateComment(notification),
                CancellationToken.None);

            _logger.LogInformation("Updated work item {workItemId} with recurrence comment", workItemId);
        }
    }

    private async Task CloseExistingNotificationAsync(GrafanaNotification notification)
    {
        AzureDevOpsAlertOptions options = _alertOptions.Value;

        using var clientRef = GetAzureDevOpsClient();
        var client = clientRef.Value;

        WorkItem existingWorkItem = await GetExistingWorkItemAsync(client, notification);

        if (existingWorkItem == null)
        {
            _logger.LogInformation("No active work item found for alert '{ruleName}', ignoring", notification.RuleName);
            return;
        }

        int workItemId = existingWorkItem.Id;
        _logger.LogInformation(
            "Found existing work item {workItemId}, resolving alert",
            workItemId);

        string currentTags = GetFieldValue(existingWorkItem, "System.Tags") ?? "";
        string newTags = UpdateTags(currentTags, addTag: InactiveAlertTag, removeTag: ActiveAlertTag);

        var fields = new Dictionary<string, string>
        {
            ["System.State"] = "Done",
            ["System.Tags"] = newTags,
        };

        await client.UpdateWorkItemAsync(options.Project, workItemId, fields, CancellationToken.None);
        await client.AddWorkItemCommentAsync(
            options.Project,
            workItemId,
            GenerateComment(notification),
            CancellationToken.None);

        _logger.LogInformation("Resolved work item {workItemId}", workItemId);
    }

    private async Task<WorkItem> GetExistingWorkItemAsync(IAzureDevOpsClient client, GrafanaNotification notification)
    {
        string id = GetUniqueIdentifier(notification);
        string automationId = string.Format(BodyLabelTextFormat, id);
        AzureDevOpsAlertOptions options = _alertOptions.Value;

        string wiql = $@"SELECT [System.Id] FROM WorkItems 
            WHERE [System.TeamProject] = '{options.Project}' 
            AND [System.AreaPath] UNDER '{options.AreaPath}' 
            AND [System.Description] CONTAINS '{automationId}' 
            AND [System.Tags] CONTAINS '{NotificationTag}' 
            ORDER BY [System.CreatedDate] DESC";

        WorkItem[] results = await client.QueryWorkItemsAsync(options.Project, wiql, CancellationToken.None);
        return results.FirstOrDefault();
    }

    internal string GenerateTitle(GrafanaNotification notification)
    {
        AzureDevOpsAlertOptions options = _alertOptions.Value;
        string title = notification.Title;

        if (!string.IsNullOrEmpty(options.TitlePrefix))
        {
            title = options.TitlePrefix + title;
        }

        return title;
    }

    internal string GenerateDescription(GrafanaNotification notification)
    {
        AzureDevOpsAlertOptions options = _alertOptions.Value;
        string metricText = BuildMetricText(notification);
        string icon = GetIcon(notification);
        string image = !string.IsNullOrEmpty(notification.ImageUrl)
            ? $"<img src=\"{notification.ImageUrl}\" alt=\"Metric Graph\" />"
            : string.Empty;

        string automationId = string.Format(BodyLabelTextFormat, GetUniqueIdentifier(notification));

        return $@"<p>:{icon}: Metric state changed to <strong>{notification.State}</strong></p>
<blockquote>{notification.Message?.Replace("\n", "<br/>")}</blockquote>
{metricText}
{image}
<p><a href=""{notification.RuleUrl}"">Go to rule</a></p>
<p>{options.SupplementalBodyText}</p>
<div style=""display:none"">{automationId}</div>".Replace("\r\n", "\n");
    }

    internal string GenerateComment(GrafanaNotification notification)
    {
        string metricText = BuildMetricText(notification);
        string icon = GetIcon(notification);
        string image = !string.IsNullOrEmpty(notification.ImageUrl)
            ? $"<img src=\"{notification.ImageUrl}\" alt=\"Metric Graph\" />"
            : string.Empty;

        return $@"<p>:{icon}: Metric state changed to <strong>{notification.State}</strong></p>
<blockquote>{notification.Message?.Replace("\n", "<br/>")}</blockquote>
{metricText}
{image}
<p><a href=""{notification.RuleUrl}"">Go to rule</a></p>".Replace("\r\n", "\n");
    }

    private static string BuildMetricText(GrafanaNotification notification)
    {
        StringBuilder metricText = new StringBuilder();
        IEnumerable<GrafanaNotificationMatch> matches = notification.EvalMatches ?? Enumerable.Empty<GrafanaNotificationMatch>();
        foreach (GrafanaNotificationMatch match in matches)
        {
            metricText.AppendLine($"<li><strong>{match.Metric}</strong> {match.Value}</li>");
        }

        if (metricText.Length > 0)
        {
            return $"<ul>{metricText}</ul>";
        }

        return string.Empty;
    }

    private static string GetIcon(GrafanaNotification notification)
    {
        return notification.State switch
        {
            "ok" => "green_heart",
            "alerting" => "broken_heart",
            "no_data" => "heavy_multiplication_x",
            "paused" => "wavy_dash",
            _ => "grey_question",
        };
    }

    private static string GetUniqueIdentifier(GrafanaNotification notification)
    {
        string id = null;
        if (notification.Tags?.TryGetValue(NotificationTagName, out id) ?? false)
        {
            return id;
        }

        return notification.RuleId.ToString();
    }

    private static string BuildTagString(params string[] tags)
    {
        return string.Join("; ", tags);
    }

    private static string UpdateTags(string currentTags, string addTag, string removeTag)
    {
        var tags = currentTags
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => !string.Equals(t, removeTag, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!tags.Contains(addTag, StringComparer.OrdinalIgnoreCase))
        {
            tags.Add(addTag);
        }

        return string.Join("; ", tags);
    }

    private static string GetFieldValue(WorkItem workItem, string fieldName)
    {
        if (workItem?.Fields != null && workItem.Fields.TryGetValue(fieldName, out object value))
        {
            return value?.ToString();
        }

        return null;
    }

    private bool IsAuthorized()
    {
        var secret = _grafanaOptions.Value?.WebhookSecret;
        if (string.IsNullOrEmpty(secret))
        {
            _logger.LogError("Grafana WebhookSecret is not configured; rejecting request");
            return false;
        }

        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
            return false;

        if (!authHeader.ToString().StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var encoded = authHeader.ToString().Substring("Basic ".Length).Trim();
            var bytes = Convert.FromBase64String(encoded);

            try
            {
                int colon = Array.IndexOf(bytes, (byte)':');
                if (colon < 0) return false;

                var provided = bytes.AsSpan(colon + 1);
                var expected = Encoding.UTF8.GetBytes(secret);

                return CryptographicOperations.FixedTimeEquals(provided, expected);
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }
        catch
        {
            return false;
        }
    }
}
