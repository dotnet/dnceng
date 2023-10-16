// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DotNet.Status.Web.Models;
using DotNet.Status.Web.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebHooks;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.DotNet.Internal.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;
using Octokit.Internal;

namespace DotNet.Status.Web.Controllers;

#nullable enable
public class GitHubHookController : ControllerBase
{
    private readonly Lazy<Task> _ensureLabels;
    private readonly IOptions<GitHubConnectionOptions> _githubOptions;
    private readonly ILogger<GitHubHookController> _logger;
    private readonly IGitHubApplicationClientFactory _gitHubApplicationClientFactory;
    private readonly IClientFactory<IAzureDevOpsClient> _azureDevOpsClientFactory;
    private readonly ITimelineIssueTriage _timelineIssueTriage;
    private readonly ITeamMentionForwarder _teamMentionForwarder;
    private readonly ISystemClock _systemClock;
    private readonly IOptionsSnapshot<RcaOptions> _rcaOptions;
    private readonly IOptionsSnapshot<MilestoneManagementOptions> _milestoneManagementOptions;

    public GitHubHookController(
        IOptions<GitHubConnectionOptions> githubOptions,
        IGitHubApplicationClientFactory gitHubApplicationClientFactory,
        IClientFactory<IAzureDevOpsClient> azureDevOpsClientFactory,
        ITimelineIssueTriage timelineIssueTriage,
        ILogger<GitHubHookController> logger,
        ITeamMentionForwarder teamMentionForwarder,
        ISystemClock systemClock,
        IOptionsSnapshot<RcaOptions> rcaOptions,
        IOptionsSnapshot<MilestoneManagementOptions> milestoneManagementOptions)
    {
        _githubOptions = githubOptions;
        _logger = logger;
        _teamMentionForwarder = teamMentionForwarder;
        _systemClock = systemClock;
        _rcaOptions = rcaOptions;
        _gitHubApplicationClientFactory = gitHubApplicationClientFactory;
        _azureDevOpsClientFactory = azureDevOpsClientFactory;
        _timelineIssueTriage = timelineIssueTriage;
        _milestoneManagementOptions = milestoneManagementOptions;
        _ensureLabels = new Lazy<Task>(EnsureLabelsAsync);
    }

    private async Task EnsureLabelsAsync()
    {
        GitHubConnectionOptions options = _githubOptions.Value;
        IGitHubClient client = await _gitHubApplicationClientFactory.CreateGitHubClientAsync(options.Organization, options.Repository);
        await GitHubModifications.TryCreateAsync(
            () => client.Issue.Labels.Create(
                options.Organization,
                options.Repository,
                new NewLabel(_githubOptions.Value.RcaLabel, "009999")),
            _logger
        );
    }

    private readonly SimpleJsonSerializer _serializer = new SimpleJsonSerializer();

    private async Task<T> DeserializeGitHubWebHook<T>()
    {
        // Octokit has a serializer and types built for github rest apis, use them
        string data;
        using (var reader = new StreamReader(Request.Body))
        {
            data = await reader.ReadToEndAsync();
        }

        var payload = _serializer.Deserialize<T>(data);
        return payload;
    }

    [GitHubWebHook(EventName = "pull_request_review_comment")]
    public async Task<IActionResult> PullRequestReviewComment()
    {
        var payload = await DeserializeGitHubWebHook<PullRequestCommentPayloadWithChanges>();

        string repo = payload.Repository.Owner.Login + "/" + payload.Repository.Name;
        int number = payload.PullRequest.Number;
        _logger.LogInformation("Received webhook for pull request {repo}#{number}", repo, number);

        string title = payload.PullRequest.Title;
        string uri = payload.Comment.HtmlUrl;
        string username = payload.Comment.User.Login;
        DateTimeOffset date = payload.Comment.UpdatedAt;
        using IDisposable scope = _logger.BeginScope("Handling pull request {repo}#{prNumber}", repo, number);
        switch (payload.Action)
        {
            case "created":
                await _teamMentionForwarder.HandleMentions(repo, null, payload.Comment.Body, title, uri, username, date);
                break;
            case "edited" when !string.IsNullOrEmpty(payload.Changes.Body?.From):
                await _teamMentionForwarder.HandleMentions(repo, payload.Changes.Body.From, payload.Comment.Body, title, uri, username, date);
                break;
        }

        return NoContent();
    }

    [GitHubWebHook(EventName = "issue_comment")]
    public async Task<IActionResult> IssueComment()
    {
        var payload = await DeserializeGitHubWebHook<IssueCommentPayloadWithChanges>();

        string repo = payload.Repository.Name;
        int number = payload.Issue.Number;
        _logger.LogInformation("Received comment webhook for issue {repo}#{number}", repo, number);

        string title = payload.Issue.Title;
        string uri = payload.Comment.HtmlUrl;
        string username = payload.Comment.User.Login;
        DateTimeOffset date = payload.Comment.UpdatedAt ?? _systemClock.UtcNow;
        using IDisposable scope = _logger.BeginScope("Handling issue {repo}#{issueNumber}", repo, number);
        switch (payload.Action)
        {
            case "created":
                await _teamMentionForwarder.HandleMentions(repo, null, payload.Comment.Body, title, uri, username, date);
                break;
            case "edited" when !string.IsNullOrEmpty(payload.Changes.Body?.From):
                await _teamMentionForwarder.HandleMentions(repo, payload.Changes.Body.From, payload.Comment.Body, title, uri, username, date);
                break;
        }

        return NoContent();
    }

    [GitHubWebHook(EventName = "pull_request")]
    public async Task<IActionResult> PullRequestHook()
    {
        var payload = await DeserializeGitHubWebHook<PullRequestEventPayloadWithChanges>();

        string repo = payload.Repository.Owner.Login + "/" + payload.Repository.Name;
        int number = payload.PullRequest.Number;
        _logger.LogInformation("Received webhook for pull request {repo}#{number}", repo, number);
        string title = payload.PullRequest.Title;
        string uri = payload.PullRequest.HtmlUrl;
        string username = payload.PullRequest.User.Login;
        DateTimeOffset date = payload.PullRequest.UpdatedAt;
        using IDisposable scope = _logger.BeginScope("Handling pull request {repo}#{prNumber}", repo, number);

        switch (payload.Action)
        {
            case "opened":
                await _teamMentionForwarder.HandleMentions(repo, null, payload.PullRequest.Body, title, uri, username,
                    date);
                break;
            case "edited" when !string.IsNullOrEmpty(payload.Changes.Body?.From):
                await _teamMentionForwarder.HandleMentions(repo, payload.Changes.Body.From, payload.PullRequest.Body, title, uri, username, date);
                break;
        }
        return NoContent();
    }

    [GitHubWebHook(EventName = "issues")]
    public async Task<IActionResult> IssuesHook()
    {
        var issueEvent = await DeserializeGitHubWebHook<IssuesHookData>();

        string action = issueEvent.Action;
        _logger.LogInformation("Processing issues action '{action}' for issue {repo}/{number}", issueEvent.Action, issueEvent.Repository.Name, issueEvent.Issue.Number);

        await ProcessNotifications(issueEvent);
        await ProcessRcaRulesAsync(issueEvent, action);
        await ProcessTimelineIssueTriageAsync(issueEvent, action);
        await ProcessEpicIssue(issueEvent, action);

        return NoContent();
    }

    private async Task ProcessEpicIssue(IssuesHookData issueEvent, string action)
    {
        string org = issueEvent.Repository.Owner.Login;
        string repo = issueEvent.Repository.Name;

        if (!_milestoneManagementOptions.Value.ReposEnabledFor.Contains($"{org}/{repo}"))
        {
            return;
        }

        IGitHubClient client = await _gitHubApplicationClientFactory.CreateGitHubClientAsync(org, repo);

        switch (action)
        {
            case "edited":
                // is there an epic label on the issue and was the title changed? If so, update the title of the milestone in the milestone list
                if (issueEvent.Issue.Labels != null && issueEvent.Issue.Labels.Any(x => x.Name == "Epic") && !string.IsNullOrEmpty(issueEvent.Changes.Title?.From))
                {
                    // check to see if the epic issue already has a milestone assigned and change that name
                    //   (in the event the milestone name is different from the epic it's associated with).
                    //   If there isn't a milestone assigned already, go see if one exists (maybe they got disconnected).
                    Milestone? foundMilestone = issueEvent.Issue.Milestone ?? await GetMilestone(issueEvent.Changes.Title.From);

                    if (foundMilestone == null)
                    {
                        await CreateMilestoneAndLinkToIssue();
                        break;
                    }

                    MilestoneUpdate update = new MilestoneUpdate
                    {
                        Title = issueEvent.Issue.Title
                    };

                    Milestone updatedMilestone = await client.Issue.Milestone.Update(org, repo, foundMilestone.Number, update);

                    IssueUpdate issueUpdate = new IssueUpdate
                    {
                        Milestone = updatedMilestone.Number
                    };

                    await client.Issue.Update(org, repo, issueEvent.Issue.Number, issueUpdate);

                }
                break;
            case "labeled":
            case "reopened":
                // was the epic label applied? check to see if the milestone name already exists. If it doesn't, create a milestone with the issue name
                if ((action == "labeled" && issueEvent.Label.Name.Equals("Epic", StringComparison.OrdinalIgnoreCase)) ||
                    (action == "reopened" && issueEvent.Issue.Labels != null && issueEvent.Issue.Labels.Any(x => x.Name == "Epic")))
                {
                    string epicName = issueEvent.Issue.Title;
                    string epicUrl = issueEvent.Issue.HtmlUrl;

                    // check to see if it currently exists
                    Milestone? foundMilestone = await GetMilestone(epicName);
                    if (foundMilestone == null)
                    {
                        await CreateMilestoneAndLinkToIssue();
                        break;
                    }

                    // Cannot create new milestones with the same name of one that exists even if it's closed, so we'll 
                    //   re-open the closed one and update the description. If it already exists for some reason, we'll 
                    //   assign the issue to the milestone, and link it in the description.

                    if (foundMilestone.State == ItemState.Closed)
                    {
                        // reopen the old one
                        MilestoneUpdate update = new MilestoneUpdate
                        {
                            State = ItemState.Open,
                            // add link to epic issue in the milestone description. 
                            Description = $"{epicUrl}\r\n\r\nPrevious Description: {foundMilestone.Description}"
                        };

                        await client.Issue.Milestone.Update(org, repo, foundMilestone.Number, update);
                    }

                    IssueUpdate issueUpdate = new IssueUpdate
                    {
                        Milestone = foundMilestone.Number
                    };

                    await client.Issue.Update(org, repo, issueEvent.Issue.Number, issueUpdate);

                }
                break;
            case "unlabeled":
                // are we trying to remove the epic label? check to see if there are any open issues (not the current issue) in the milestone.
                if (issueEvent.Label.Name.Equals("Epic", StringComparison.OrdinalIgnoreCase) && issueEvent.Issue.Milestone?.OpenIssues > 1)
                {
                    await client.Issue.Labels.AddToIssue(org, repo, issueEvent.Issue.Number, new string[] { "Epic" });
                    _logger.LogInformation("Issue {Organization}/{Repository}#{IssueNumber} is the epic of a milestone that currently contains open issues. Adding the Epic label back to this issue", org, repo, issueEvent.Issue.Number);
                    await CommentOnIssue();
                }
                else if (issueEvent.Label.Name.Equals("Epic") && issueEvent.Issue.Milestone?.OpenIssues == 1)
                {
                    int currentMilestone = issueEvent.Issue.Milestone.Number;

                    // Remove issue from milestone
                    IssueUpdate issueUpdate = new IssueUpdate
                    {
                        Milestone = null
                    };

                    await client.Issue.Update(org, repo, issueEvent.Issue.Number, issueUpdate);

                    // Close milestone
                    var milestoneUpdate = new MilestoneUpdate
                    {
                        State = ItemState.Closed
                    };

                    await client.Issue.Milestone.Update(org, repo, currentMilestone, milestoneUpdate);

                }
                break;
            case "closed":
                // is there an epic label on the issue?
                if (issueEvent.Issue.Labels.Any(x => x.Name.Equals("Epic", StringComparison.OrdinalIgnoreCase)) && issueEvent.Issue.Milestone != null)
                {
                    // Are there open issues in the associated milestone? if yes, reopen the issue. 
                    if (issueEvent.Issue.Milestone.OpenIssues > 0)
                    {
                        IssueUpdate issueUpdate = new IssueUpdate { State = ItemState.Open };

                        await client.Issue.Update(org, repo, issueEvent.Issue.Number, issueUpdate);
                        _logger.LogInformation("Issue {Organization}/{Repository}#{IssueNumber} is the epic of a milestone that currently contains open issues. Re-opening this issue.", org, repo, issueEvent.Issue.Number);
                        await CommentOnIssue();
                        break;
                    }

                    // If we close the issue, close the milestone, too
                    var milestoneUpdate = new MilestoneUpdate
                    {
                        State = ItemState.Closed
                    };

                    await client.Issue.Milestone.Update(org, repo, issueEvent.Issue.Milestone.Number, milestoneUpdate);

                }
                break;
        }

        async Task CommentOnIssue()
        {
            await client.Issue.Comment.Create(org, repo, issueEvent.Issue.Number, "Sorry! Could not close or remove the 'Epic' label from this issue because there are still open issues associated with it. Close or remove open issues in the related milestone and try again.");
        }

        async Task<Milestone?> GetMilestone(string milestoneName)
        {
            MilestoneRequest requestOptions = new MilestoneRequest
            {
                State = ItemStateFilter.All
            };
            IEnumerable<Milestone> milestones = await client.Issue.Milestone.GetAllForRepository(org, repo, requestOptions);

            return milestones.FirstOrDefault(x => x.Title.Equals(milestoneName));
        }

        async Task CreateMilestoneAndLinkToIssue()
        {
            // create new milestone with epic title
            NewMilestone newMilestone = new NewMilestone(issueEvent.Issue.Title)
            {
                // add link to epic issue in the milestone description
                Description = issueEvent.Issue.HtmlUrl,
                State = ItemState.Open
            };

            Milestone milestone = await client.Issue.Milestone.Create(org, repo, newMilestone);

            IssueUpdate issueUpdate = new IssueUpdate
            {
                Milestone = milestone.Number
            };

            await client.Issue.Update(org, repo, issueEvent.Issue.Number, issueUpdate);
        }
    }

    private async Task ProcessNotifications(IssuesHookData issueEvent)
    {
        string repo = issueEvent.Repository.Owner.Login + "/" + issueEvent.Repository.Name;
        int number = issueEvent.Issue.Number;
        string title = issueEvent.Issue.Title;
        string uri = issueEvent.Issue.HtmlUrl;
        string username = issueEvent.Issue.User.Login;
        DateTimeOffset date = issueEvent.Issue.UpdatedAt ?? _systemClock.UtcNow;
        using IDisposable scope = _logger.BeginScope("Handling issue {repo}#{issueNumber}", repo, number);
        switch (issueEvent.Action)
        {
            case "opened":
                await _teamMentionForwarder.HandleMentions(repo, null, issueEvent.Issue.Body, title, uri, username,
                    date);
                break;
            case "edited":
                await _teamMentionForwarder.HandleMentions(repo, issueEvent.Changes.Body?.From, issueEvent.Issue.Body, title, uri, username, date);
                break;
        }
    }

    public static JsonSerializerOptions SerializerOptions()
    {
        JsonSerializerOptions options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        return options;
    }

    private async Task ProcessTimelineIssueTriageAsync(IssuesHookData data, string action)
    {
        await _timelineIssueTriage.ProcessIssueEvent(data);
    }

    private async Task ProcessRcaRulesAsync(IssuesHookData data, string action)
    {
        if (!ShouldOpenRcaIssue(data, action, out string? triggeringLabel))
        {
            return;
        }

        GitHubConnectionOptions options = _githubOptions.Value;

        int issueNumber = data.Issue.Number;
        string issueTitle = data.Issue.Title;
        string? assignee = data.Issue.Assignee?.Login;

        string[] copiedLabels = Array.Empty<string>();

        if (options.RcaCopyLabelPrefixes != null && options.RcaCopyLabelPrefixes.Length > 0)
        {
            copiedLabels = data.Issue
                .Labels
                .Select(l => l.Name)
                .Where(n => options.RcaCopyLabelPrefixes.Any(o =>
                    n.StartsWith(o, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            _logger.LogInformation($"Copying labels: '{string.Join("', '", copiedLabels)}'");
        }

        if (string.IsNullOrEmpty(assignee))
        {
            _logger.LogInformation("Issue was not assigned, using event sender");
            assignee = data.Sender.Login;
        }

        _logger.LogInformation("Opening RCA work item in Azure Boards {org}/{project}", _rcaOptions.Value.Organization, _rcaOptions.Value.Project);

        // The RCA template has all of the sections that we want to be filled out, so we don't need to specify the text here
        using var azureDevOpsClient = _azureDevOpsClientFactory.GetClient(_rcaOptions.Value.Organization);
        WorkItem? workItem = await azureDevOpsClient.Value.CreateRcaWorkItem(_rcaOptions.Value.Project, $"RCA: {issueTitle} ({issueNumber})");
        _logger.LogInformation("Successfully opened work item {number}: {url}", workItem?.Id, workItem?.Links.Html.Href);

        string issueRepo = data.Repository.Name;
        string issueOrg = data.Repository.Owner.Login;
        _logger.LogInformation("Opening connection to open issue to {org}/{repo}", options.Organization, options.Repository);
        IGitHubClient client = await _gitHubApplicationClientFactory.CreateGitHubClientAsync(options.Organization, options.Repository);

        var issue = new NewIssue($"RCA: {issueTitle} ({issueNumber})")
        {
            Body =
                $@"An issue, {issueOrg}/{issueRepo}#{issueNumber}, that was marked with the '{triggeringLabel}' label was recently closed.

Please fill out the root cause analysis [Azure Boards work item]({workItem?.Links.Html.Href}), and then close this issue and the Azure Boards work item.

Filling it out promptly after resolving an issue ensures things are fresh in your mind.

For help filling out this form, see the [Root Cause Analysis](https://dev.azure.com/dnceng/internal/_wiki/wikis/DNCEng%20Services%20Wiki/878/Root-Cause-Analysis).
",
        };

        if (!string.IsNullOrEmpty(assignee))
        {
            _logger.LogInformation("Setting assignee");
            issue.Assignees.Add(assignee);
        }

        await _ensureLabels.Value;

        if (options.RcaLabel != null)
        {
            _logger.LogTrace("Adding label '{label}'", options.RcaLabel);
            issue.Labels.Add(options.RcaLabel);
        }

        foreach (string toCopy in copiedLabels)
        {
            issue.Labels.Add(toCopy);
        }

        _logger.LogInformation("Sending issue create request...");
        Issue createdIssue = await client.Issue.Create(options.Organization, options.Repository, issue);

        _logger.LogInformation("Created RCA issue {number}", createdIssue.Number);
    }

    private bool ShouldOpenRcaIssue(IssuesHookData data, string action, out string? triggeringLabel)
    {
        triggeringLabel = null;
        GitHubConnectionOptions options = _githubOptions.Value;
        if (options.RcaRequestedLabels == null || options.RcaRequestedLabels.Length == 0)
        {
            return false;
        }

        switch (action)
        {
            case "closed":
                HashSet<string> names = data.Issue.Labels.Select(l => l.Name).ToHashSet();
                names.IntersectWith(options.RcaRequestedLabels);
                triggeringLabel = names.FirstOrDefault();
                if (names.Count == 0)
                {
                    _logger.LogTrace("Issue {repo}/{number} is closed but has no RCA label, taking no RCA action", data.Repository.Name, data.Issue.Number);
                    return false;
                }

                _logger.LogInformation("Issue closed with label '{label}', RCA required", triggeringLabel);
                return true;

            case "labeled":

                triggeringLabel = data.Label.Name;
                if (data.Issue.State == ItemState.Open)
                {
                    _logger.LogInformation("Issue {repo}/{number} is labeled with {label} but still open, taking no RCA action", data.Repository.Name, data.Issue.Number, triggeringLabel);
                    return false;
                }

                if (!options.RcaRequestedLabels.Contains(data.Label.Name, StringComparer.OrdinalIgnoreCase))
                {
                    _logger.LogTrace("Label '{label}' irrelevant, taking no RCA action", triggeringLabel);
                    return false;
                }

                _logger.LogInformation("Previously closed labeled with '{label}', RCA required", triggeringLabel);
                return true;

            default:
                _logger.LogTrace("Issues hook with '{action}' action, no RCA action taken", action);
                return false;
        }
    }

    [GitHubWebHook]
    public IActionResult AcceptHook()
    {
        // Ignore them, none are interesting
        return NoContent();
    }
}
