// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Status.Web.Models;
using DotNet.Status.Web.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.DotNet.Internal.DependencyInjection;
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
    public const string BodyLabelTextFormat = "Grafana-Automated-Alert-Id-{0}";
    public const string NotificationTagName = "NotificationId";

    private readonly IOptions<AzureDevOpsAlertOptions> _azureDevOpsOptions;
    private readonly IClientFactory<IAzureDevOpsClient> _azureDevOpsClientFactory;
    private readonly ILogger<AlertHookController> _logger;

    public AlertHookController(
        IClientFactory<IAzureDevOpsClient> azureDevOpsClientFactory,
        IOptions<AzureDevOpsAlertOptions> azureDevOpsOptions,
        ILogger<AlertHookController> logger)
    {
        _azureDevOpsClientFactory = azureDevOpsClientFactory;
        _azureDevOpsOptions = azureDevOpsOptions;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> NotifyAsync(GrafanaNotification notification)
    {
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

    private async Task OpenNewNotificationAsync(GrafanaNotification notification)
    {
        string organization = _azureDevOpsOptions.Value.Organization;
        string project = _azureDevOpsOptions.Value.Project;
        _logger.LogInformation(
            "Alert state detected for {ruleUrl} in stage {ruleState}, porting to Azure DevOps project {organization}/{project}",
            notification.RuleUrl,
            notification.State,
            organization,
            project);

        using Reference<IAzureDevOpsClient> clientRef = GetAzureDevOpsClient();
        IAzureDevOpsClient client = clientRef.Value;
        
        WorkItem? existingWorkItem = await GetExistingWorkItemAsync(client, notification);
        
        if (existingWorkItem == null)
        {
            _logger.LogInformation("No existing work item found, creating new active work item with tag {tag}",
                ActiveAlertTag);
            
            string title = GenerateWorkItemTitle(notification);
            string description = GenerateWorkItemDescription(notification);
            string[] tags = GenerateWorkItemTags(notification, true);
            
            WorkItem? workItem = await client.CreateAlertWorkItem(project, title, description, tags, CancellationToken.None);
            _logger.LogInformation("Azure DevOps work item {workItemId} created in project {project}", 
                workItem?.Id, project);
        }
        else
        {
            _logger.LogInformation(
                "Found existing work item {workItemId}, replacing {inactiveTag} with {activeTag}",
                existingWorkItem.Id,
                InactiveAlertTag,
                ActiveAlertTag);

            await client.UpdateWorkItemTags(project, existingWorkItem.Id, 
                new[] { ActiveAlertTag }, 
                new[] { InactiveAlertTag }, 
                CancellationToken.None);

            _logger.LogInformation("Adding recurrence comment to work item {workItemId}",
                existingWorkItem.Id);
            
            string comment = GenerateNewNotificationComment(notification);
            await client.AddWorkItemComment(project, existingWorkItem.Id, comment, CancellationToken.None);
            
            _logger.LogInformation("Created comment on work item {workItemId}", existingWorkItem.Id);
        }
    }

    private string GenerateNewNotificationComment(GrafanaNotification notification)
    {
        StringBuilder metricText = new StringBuilder();
        foreach (GrafanaNotificationMatch match in notification.EvalMatches)
        {
            metricText.AppendLine($"  - {match.Metric} {match.Value}");
        }
            
        string icon = GetIcon(notification);
        string image = !string.IsNullOrEmpty(notification.ImageUrl) ? $"<img src=\"{notification.ImageUrl}\" alt=\"Metric Graph\" />" : string.Empty;

        return $@":{icon}: Metric state changed to {notification.State}

{notification.Message?.Replace("\n", "\n")}

{metricText}

{image}

<a href=\"{notification.RuleUrl}\">Go to rule</a>".Replace("\r\n","\n");
    }

    private string GenerateWorkItemTitle(GrafanaNotification notification)
    {
        string issueTitle = notification.Title;

        AzureDevOpsAlertOptions options = _azureDevOpsOptions.Value;
        string prefix = options.TitlePrefix;
        if (prefix != null)
        {
            issueTitle = prefix + issueTitle;
        }

        return issueTitle;
    }

    private string GenerateWorkItemDescription(GrafanaNotification notification)
    {
        StringBuilder metricText = new StringBuilder();
        foreach (GrafanaNotificationMatch match in notification.EvalMatches)
        {
            metricText.AppendLine($"  - {match.Metric} {match.Value}");
        }

        string icon = GetIcon(notification);
        string image = !string.IsNullOrEmpty(notification.ImageUrl) ? $"<img src=\"{notification.ImageUrl}\" alt=\"Metric Graph\" />" : string.Empty;

        AzureDevOpsAlertOptions options = _azureDevOpsOptions.Value;
        
        string notificationTargets = string.Empty;
        if (options.NotificationTargets != null && options.NotificationTargets.Length > 0)
        {
            notificationTargets = $"{string.Join(", ", options.NotificationTargets.Select(target => $"@{target}"))}, please investigate";
        }

        return $@":{icon}: Metric state changed to {notification.State}

{notification.Message?.Replace("\n", "\n")}

{metricText}

{image}

<a href=\"{notification.RuleUrl}\">Go to rule</a>

{notificationTargets}

{options.SupplementalBodyText}

<div style='display:none'>
Automation information below, do not change

{string.Format(BodyLabelTextFormat, GetUniqueIdentifier(notification))}
</div>
".Replace("\r\n","\n");
    }

    private string[] GenerateWorkItemTags(GrafanaNotification notification, bool isActive)
    {
        List<string> tags = new List<string>();
        
        tags.Add(NotificationIdTag);
        tags.Add(isActive ? ActiveAlertTag : InactiveAlertTag);
        
        AzureDevOpsAlertOptions options = _azureDevOpsOptions.Value;
        
        if (options.AlertTags != null)
        {
            tags.AddRange(options.AlertTags);
        }

        if (options.EnvironmentTags != null)
        {
            tags.AddRange(options.EnvironmentTags);
        }

        return tags.ToArray();
    }

    private static string GetIcon(GrafanaNotification notification)
    {
        string icon;
        switch (notification.State)
        {
            case "ok":
                icon = "green_heart";
                break;
            case "alerting":
                icon = "broken_heart";
                break;
            case "no_data":
                icon = "heavy_multiplication_x";
                break;
            case "paused":
                icon = "wavy_dash";
                break;
            default:
                icon = "grey_question";
                break;
        }

        return icon;
    }

    private async Task CloseExistingNotificationAsync(GrafanaNotification notification)
    {
        string organization = _azureDevOpsOptions.Value.Organization;
        string project = _azureDevOpsOptions.Value.Project;
        
        using Reference<IAzureDevOpsClient> clientRef = GetAzureDevOpsClient();
        IAzureDevOpsClient client = clientRef.Value;
        
        WorkItem? workItem = await GetExistingWorkItemAsync(client, notification);
        if (workItem == null)
        {
            _logger.LogInformation("No active work item found for alert '{ruleName}', ignoring", notification.RuleName);
            return;
        }

        _logger.LogInformation(
            "Found existing work item {workItemId}, replacing {activeTag} with {inactiveTag}",
            workItem.Id,
            ActiveAlertTag,
            InactiveAlertTag);

        await client.UpdateWorkItemTags(project, workItem.Id, 
            new[] { InactiveAlertTag }, 
            new[] { ActiveAlertTag }, 
            CancellationToken.None);

        _logger.LogInformation("Adding recurrence comment to work item {workItemId}",
            workItem.Id);
        
        string comment = GenerateNewNotificationComment(notification);
        await client.AddWorkItemComment(project, workItem.Id, comment, CancellationToken.None);
        
        _logger.LogInformation("Created comment on work item {workItemId}", workItem.Id);
    }

    private async Task<WorkItem?> GetExistingWorkItemAsync(IAzureDevOpsClient client, GrafanaNotification notification)
    {
        string id = GetUniqueIdentifier(notification);
        string project = _azureDevOpsOptions.Value.Project;

        string searchTag = NotificationIdTag;
        
        WorkItem[]? workItems = await client.QueryWorkItemsByTag(project, searchTag, CancellationToken.None);
        
        if (workItems == null || workItems.Length == 0)
        {
            return null;
        }

        string automationId = string.Format(BodyLabelTextFormat, id);
        
        foreach (WorkItem workItem in workItems)
        {
            if (workItem.Fields.TryGetValue("System.Description", out object? descriptionObj))
            {
                string description = descriptionObj?.ToString() ?? string.Empty;
                if (description.Contains(automationId))
                {
                    AzureDevOpsAlertOptions options = _azureDevOpsOptions.Value;
                    if (options.EnvironmentTags != null && options.EnvironmentTags.Length > 0)
                    {
                        if (workItem.Fields.TryGetValue("System.Tags", out object? tagsObj))
                        {
                            string tags = tagsObj?.ToString() ?? string.Empty;
                            bool hasAllEnvironmentTags = options.EnvironmentTags.All(envTag => 
                                tags.Split(new[] { "; " }, System.StringSplitOptions.RemoveEmptyEntries).Contains(envTag));
                            
                            if (hasAllEnvironmentTags)
                            {
                                return workItem;
                            }
                        }
                    }
                    else
                    {
                        return workItem;
                    }
                }
            }
        }

        return null;
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

    private Reference<IAzureDevOpsClient> GetAzureDevOpsClient()
    {
        string organization = _azureDevOpsOptions.Value.Organization;
        _logger.LogInformation("Getting AzureDevOpsClient for org {organization}", organization);
        return _azureDevOpsClientFactory.GetClient($"alert/{organization}");
    }
}
