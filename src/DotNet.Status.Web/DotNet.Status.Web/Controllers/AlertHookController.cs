// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Status.Web.Models;
using DotNet.Status.Web.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNet.Status.Web.Controllers;

[ApiController]
[Route("api/alert")]
public class AlertHookController : ControllerBase
{
    public const string NotificationIdTag = "Grafana Alert";
    public const string ActiveAlertTag = "Active Alert";
    public const string InactiveAlertTag = "Inactive Alert";
    public const string BodyAutomationIdFormat = "Grafana-Automated-Alert-Id-{0}";
    public const string NotificationTagName = "NotificationId";

    private readonly IOptions<GrafanaAlertOptions> _alertOptions;
    private readonly ILogger<AlertHookController> _logger;
    private readonly IAzureDevOpsWorkItemClient _workItemClient;

    public AlertHookController(
        IAzureDevOpsWorkItemClient workItemClient,
        IOptions<GrafanaAlertOptions> alertOptions,
        ILogger<AlertHookController> logger)
    {
        _workItemClient = workItemClient;
        _alertOptions = alertOptions;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> NotifyAsync(GrafanaNotification notification, CancellationToken cancellationToken)
    {
        switch (notification.State)
        {
            case "ok":
                await CloseExistingNotificationAsync(notification, cancellationToken);
                break;
            case "alerting":
            case "no_data":
                await OpenNewNotificationAsync(notification, cancellationToken);
                break;
            default:
                return BadRequest();
        }

        return NoContent();
    }

    private async Task OpenNewNotificationAsync(GrafanaNotification notification, CancellationToken cancellationToken)
    {
        GrafanaAlertOptions options = _alertOptions.Value;
        _logger.LogInformation(
            "Alert state detected for {ruleUrl} in state {ruleState}, creating Azure DevOps work item in {org}/{project}",
            notification.RuleUrl,
            notification.State,
            options.Organization,
            options.Project);

        int? existingId = await FindExistingWorkItemAsync(notification, cancellationToken);
        if (existingId == null)
        {
            _logger.LogInformation("No existing work item found, creating new work item with tag {tag}", ActiveAlertTag);

            Dictionary<string, object> fields = new Dictionary<string, object>
            {
                ["System.Title"] = BuildTitle(notification, options),
                ["System.Description"] = GenerateWorkItemDescription(notification),
                ["System.AreaPath"] = options.AreaPath,
                ["System.Tags"] = BuildInitialTags(options),
            };

            int workItemId = await _workItemClient.CreateWorkItemAsync(options.Project, options.WorkItemType, fields, cancellationToken);
            _logger.LogInformation("Azure DevOps work item {workItemId} created", workItemId);
        }
        else
        {
            _logger.LogInformation(
                "Found existing work item {workItemId}, replacing tag {inactiveTag} with {activeTag}",
                existingId.Value,
                InactiveAlertTag,
                ActiveAlertTag);

            Dictionary<string, string> currentFields = await _workItemClient.GetWorkItemFieldsAsync(existingId.Value, cancellationToken);
            string currentTags = currentFields.GetValueOrDefault("System.Tags", string.Empty);
            string updatedTags = AddTag(RemoveTag(currentTags, InactiveAlertTag), ActiveAlertTag);

            await _workItemClient.UpdateWorkItemFieldsAsync(existingId.Value, new Dictionary<string, object>
            {
                ["System.Tags"] = updatedTags,
            }, cancellationToken);

            _logger.LogInformation("Adding recurrence comment to work item {workItemId}", existingId.Value);
            await _workItemClient.AddCommentAsync(options.Project, existingId.Value, GenerateCommentHtml(notification), cancellationToken);
            _logger.LogInformation("Updated work item {workItemId} and added comment", existingId.Value);
        }
    }

    private async Task CloseExistingNotificationAsync(GrafanaNotification notification, CancellationToken cancellationToken)
    {
        GrafanaAlertOptions options = _alertOptions.Value;
        int? existingId = await FindExistingWorkItemAsync(notification, cancellationToken);
        if (existingId == null)
        {
            _logger.LogInformation("No active work item found for alert '{ruleName}', ignoring", notification.RuleName);
            return;
        }

        _logger.LogInformation(
            "Found existing work item {workItemId}, replacing tag {activeTag} with {inactiveTag}",
            existingId.Value,
            ActiveAlertTag,
            InactiveAlertTag);

        Dictionary<string, string> currentFields = await _workItemClient.GetWorkItemFieldsAsync(existingId.Value, cancellationToken);
        string currentTags = currentFields.GetValueOrDefault("System.Tags", string.Empty);
        string updatedTags = AddTag(RemoveTag(currentTags, ActiveAlertTag), InactiveAlertTag);

        await _workItemClient.UpdateWorkItemFieldsAsync(existingId.Value, new Dictionary<string, object>
        {
            ["System.Tags"] = updatedTags,
        }, cancellationToken);

        _logger.LogInformation("Adding recurrence comment to work item {workItemId}", existingId.Value);
        await _workItemClient.AddCommentAsync(options.Project, existingId.Value, GenerateCommentHtml(notification), cancellationToken);
        _logger.LogInformation("Updated work item {workItemId} and added comment", existingId.Value);
    }

    private async Task<int?> FindExistingWorkItemAsync(GrafanaNotification notification, CancellationToken cancellationToken)
    {
        GrafanaAlertOptions options = _alertOptions.Value;
        string automationId = string.Format(BodyAutomationIdFormat, GetUniqueIdentifier(notification));
        string escapedId = automationId.Replace("'", "''");
        string wiql = $"SELECT [System.Id] FROM WorkItems " +
                      $"WHERE [System.TeamProject] = '{options.Project}' " +
                      $"AND [System.Description] CONTAINS '{escapedId}' " +
                      $"AND [System.State] <> 'Closed' " +
                      $"ORDER BY [System.CreatedDate] DESC";

        int[] ids = await _workItemClient.QueryWorkItemsByWiqlAsync(options.Project, wiql, cancellationToken);
        return ids.Length > 0 ? ids[0] : null;
    }

    private static string BuildTitle(GrafanaNotification notification, GrafanaAlertOptions options)
    {
        string title = notification.Title;
        if (!string.IsNullOrEmpty(options.TitlePrefix))
        {
            title = options.TitlePrefix + title;
        }
        return title;
    }

    private static string BuildInitialTags(GrafanaAlertOptions options)
    {
        List<string> tags = new List<string> { NotificationIdTag, ActiveAlertTag };
        tags.AddRange(options.EnvironmentTags.OrEmpty());
        tags.AddRange(options.AlertTags.OrEmpty());
        return string.Join("; ", tags.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private string GenerateWorkItemDescription(GrafanaNotification notification)
    {
        GrafanaAlertOptions options = _alertOptions.Value;
        StringBuilder sb = new StringBuilder();

        string stateIcon = GetStateIcon(notification.State);
        sb.Append($"<p>{stateIcon} Metric state changed to <strong>{WebUtility.HtmlEncode(notification.State)}</strong></p>");

        if (!string.IsNullOrEmpty(notification.Message))
        {
            sb.Append($"<blockquote>{WebUtility.HtmlEncode(notification.Message)}</blockquote>");
        }

        if (notification.EvalMatches?.Count > 0)
        {
            sb.Append("<ul>");
            foreach (GrafanaNotificationMatch match in notification.EvalMatches)
            {
                sb.Append($"<li><em>{WebUtility.HtmlEncode(match.Metric)}</em> {match.Value}</li>");
            }
            sb.Append("</ul>");
        }

        if (!string.IsNullOrEmpty(notification.ImageUrl))
        {
            sb.Append($"<p><img src=\"{WebUtility.HtmlEncode(notification.ImageUrl)}\" alt=\"Metric Graph\"/></p>");
        }

        sb.Append($"<p><a href=\"{WebUtility.HtmlEncode(notification.RuleUrl)}\">Go to rule</a></p>");

        if (!string.IsNullOrEmpty(options.SupplementalBodyText))
        {
            sb.Append($"<p>{WebUtility.HtmlEncode(options.SupplementalBodyText)}</p>");
        }

        string automationId = string.Format(BodyAutomationIdFormat, GetUniqueIdentifier(notification));
        sb.Append($"<p><em>{automationId}</em></p>");

        return sb.ToString();
    }

    private static string GenerateCommentHtml(GrafanaNotification notification)
    {
        StringBuilder sb = new StringBuilder();

        string stateIcon = GetStateIcon(notification.State);
        sb.Append($"<p>{stateIcon} Metric state changed to <strong>{WebUtility.HtmlEncode(notification.State)}</strong></p>");

        if (!string.IsNullOrEmpty(notification.Message))
        {
            sb.Append($"<blockquote>{WebUtility.HtmlEncode(notification.Message)}</blockquote>");
        }

        if (notification.EvalMatches?.Count > 0)
        {
            sb.Append("<ul>");
            foreach (GrafanaNotificationMatch match in notification.EvalMatches)
            {
                sb.Append($"<li><em>{WebUtility.HtmlEncode(match.Metric)}</em> {match.Value}</li>");
            }
            sb.Append("</ul>");
        }

        if (!string.IsNullOrEmpty(notification.ImageUrl))
        {
            sb.Append($"<p><img src=\"{WebUtility.HtmlEncode(notification.ImageUrl)}\" alt=\"Metric Graph\"/></p>");
        }

        sb.Append($"<p><a href=\"{WebUtility.HtmlEncode(notification.RuleUrl)}\">Go to rule</a></p>");

        return sb.ToString();
    }

    private static string GetStateIcon(string state)
    {
        return state switch
        {
            "ok" => "💚",
            "alerting" => "💔",
            "no_data" => "✖",
            "paused" => "〰",
            _ => "❓",
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

    private static string AddTag(string existingTags, string tagToAdd)
    {
        List<string> tags = ParseTagsList(existingTags);
        if (!tags.Any(t => string.Equals(t, tagToAdd, StringComparison.OrdinalIgnoreCase)))
        {
            tags.Add(tagToAdd);
        }
        return string.Join("; ", tags);
    }

    private static string RemoveTag(string existingTags, string tagToRemove)
    {
        List<string> tags = ParseTagsList(existingTags);
        tags.RemoveAll(t => string.Equals(t, tagToRemove, StringComparison.OrdinalIgnoreCase));
        return string.Join("; ", tags);
    }

    private static List<string> ParseTagsList(string tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return new List<string>();
        }
        return tags.Split(';')
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();
    }
}
