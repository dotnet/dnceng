using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using DotNet.Status.Web.Controllers;
using DotNet.Status.Web.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebHooks.Filters;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.DotNet.Internal.DependencyInjection;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Octokit;

namespace DotNet.Status.Web.Tests;

public class TestVerifySignatureFilter : GitHubVerifySignatureFilter, IAsyncResourceFilter
{
#pragma warning disable 618
    public TestVerifySignatureFilter(IConfiguration configuration, IHostingEnvironment hostingEnvironment, ILoggerFactory loggerFactory) : base(configuration, hostingEnvironment, loggerFactory)
#pragma warning restore 618
    {
    }

    public new async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        await next();
    }
}

[TestFixture]
public class GitHubHookControllerTests
{
    #region Team Mention Forwarder Tests

    public static string TestTeamsWebHookUri = "https://example.teams/webhook/sha";
    public static string TestAzdoWebHookUri = "https://example.azdo/webhook/api";
    public static string WatchedTeam = "test-user/watched-team";
    public static string IgnoredRepo = "test-user/ignored";

    [Test]
    public async Task NewIssueWithMentionNotifies()
    {
        var data = new JObject
        {
            ["action"] = "opened",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["issue"] = new JObject
            {
                ["number"] = 2,
                ["body"] = $"Something pizza @{WatchedTeam}",
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
            },
        };
        var eventName = "issues";
        await SendWebHook(data, eventName, true);
    }

    [Test]
    public async Task NewIssueWithoutMentionDoesntNotify()
    {
        var data = new JObject
        {
            ["action"] = "opened",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["issue"] = new JObject
            {
                ["number"] = 2,
                ["body"] = "Something pizza",
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
            },
        };
        var eventName = "issues";
        await SendWebHook(data, eventName, false);
    }

    [Test]
    public async Task EditedIssueWithNewMentionNotifies()
    {
        var data = new JObject
        {
            ["action"] = "edited",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["issue"] = new JObject
            {
                ["number"] = 2,
                ["body"] = $"Something pizza @{WatchedTeam}",
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
            },
            ["changes"] = new JObject
            {
                ["body"] = new JObject
                {
                    ["from"] = "Something pizza",
                },
            },
        };
        var eventName = "issues";
        await SendWebHook(data, eventName, true);
    }

    [Test]
    public async Task EditedIssueWithExistingMentionDoesntNotify()
    {
        var data = new JObject
        {
            ["action"] = "edited",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["issue"] = new JObject
            {
                ["number"] = 2,
                ["body"] = $"Something pizza @{WatchedTeam}",
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
            },
            ["changes"] = new JObject
            {
                ["body"] = new JObject
                {
                    ["from"] = $"Something @{WatchedTeam} pizza",
                },
            },
        };
        var eventName = "issues";
        await SendWebHook(data, eventName, false);
    }

    [Test]
    public async Task EditedIssueWithRemovedMentionDoesntNotify()
    {
        var data = new JObject
        {
            ["action"] = "edited",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["issue"] = new JObject
            {
                ["number"] = 2,
                ["body"] = "Something pizza",
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
            },
            ["changes"] = new JObject
            {
                ["body"] = new JObject
                {
                    ["from"] = $"Something @{WatchedTeam} pizza",
                },
            },
        };
        var eventName = "issues";
        await SendWebHook(data, eventName, false);
    }

    [Test]
    public async Task EditedIssueWithNoMentionDoesntNotify()
    {
        var data = new JObject
        {
            ["action"] = "edited",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["issue"] = new JObject
            {
                ["number"] = 2,
                ["body"] = "Something pizza",
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
            },
            ["changes"] = new JObject
            {
                ["body"] = new JObject
                {
                    ["from"] = "Something pineapple pizza",
                },
            },
        };
        var eventName = "issues";
        await SendWebHook(data, eventName, false);
    }

    [Test]
    public async Task NewPullRequestWithMentionNotifies()
    {
        var data = new JObject
        {
            ["action"] = "opened",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["pull_request"] = new JObject
            {
                ["number"] = 2,
                ["body"] = $"Something pizza @{WatchedTeam}",
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
            },
        };
        var eventName = "pull_request";
        await SendWebHook(data, eventName, true);
    }

    [Test]
    public async Task NewPullRequestWithoutMentionDoesntNotify()
    {
        var data = new JObject
        {
            ["action"] = "opened",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["pull_request"] = new JObject
            {
                ["number"] = 2,
                ["body"] = "Something pizza",
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
            },
        };
        var eventName = "pull_request";
        await SendWebHook(data, eventName, false);
    }

    [Test]
    public async Task EditedPullRequestWithNewMentionNotifies()
    {
        var data = new JObject
        {
            ["action"] = "edited",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["pull_request"] = new JObject
            {
                ["number"] = 2,
                ["body"] = $"Something pizza @{WatchedTeam}",
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
            },
            ["changes"] = new JObject
            {
                ["body"] = new JObject
                {
                    ["from"] = "Something pizza",
                },
            },
        };
        var eventName = "pull_request";
        await SendWebHook(data, eventName, true);
    }

    [Test]
    public async Task EditedPullRequestWithExistingMentionDoesntNotify()
    {
        var data = new JObject
        {
            ["action"] = "edited",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["pull_request"] = new JObject
            {
                ["number"] = 2,
                ["body"] = $"Something pizza @{WatchedTeam}",
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
            },
            ["changes"] = new JObject
            {
                ["body"] = new JObject
                {
                    ["from"] = $"Something @{WatchedTeam} pizza",
                },
            },
        };
        var eventName = "pull_request";
        await SendWebHook(data, eventName, false);
    }

    [Test]
    public async Task EditedPullRequestWithRemovedMentionDoesntNotify()
    {
        var data = new JObject
        {
            ["action"] = "edited",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["pull_request"] = new JObject
            {
                ["number"] = 2,
                ["body"] = $"Something pizza",
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
            },
            ["changes"] = new JObject
            {
                ["body"] = new JObject
                {
                    ["from"] = $"Something @{WatchedTeam} pizza",
                },
            },
        };
        var eventName = "pull_request";
        await SendWebHook(data, eventName, false);
    }

    [Test]
    public async Task EditedPullRequestWithNoMentionDoesntNotify()
    {
        var data = new JObject
        {
            ["action"] = "edited",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["pull_request"] = new JObject
            {
                ["number"] = 2,
                ["body"] = $"Something pizza",
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
            },
            ["changes"] = new JObject
            {
                ["body"] = new JObject
                {
                    ["from"] = "Something pineapple pizza",
                },
            },
        };
        var eventName = "pull_request";
        await SendWebHook(data, eventName, false);
    }

    [Test]
    public async Task NewIssueCommentWithMentionNotifies()
    {
        var data = new JObject
        {
            ["action"] = "created",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["issue"] = new JObject
            {
                ["number"] = 2,
            },
            ["comment"] = new JObject
            {
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
                ["body"] = $"Something pizza @{WatchedTeam}",
            },
        };
        var eventName = "issue_comment";
        await SendWebHook(data, eventName, true);
    }

    [Test]
    public async Task NewIssueCommentWithoutMentionDoesntNotify()
    {
        var data = new JObject
        {
            ["action"] = "created",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["issue"] = new JObject
            {
                ["number"] = 2,
            },
            ["comment"] = new JObject
            {
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
                ["body"] = $"Something pizza",
            },
        };
        var eventName = "issue_comment";
        await SendWebHook(data, eventName, false);
    }

    [Test]
    public async Task EditedIssueCommentWithNewMentionNotifies()
    {
        var data = new JObject
        {
            ["action"] = "edited",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["issue"] = new JObject
            {
                ["number"] = 2,
            },
            ["comment"] = new JObject
            {
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
                ["body"] = $"Something pizza @{WatchedTeam}",
            },
            ["changes"] = new JObject
            {
                ["body"] = new JObject
                {
                    ["from"] = "Something pizza",
                },
            },
        };
        var eventName = "issue_comment";
        await SendWebHook(data, eventName, true);
    }


    [Test]
    public async Task EditedIssueCommentWithTeamButNotMentionDoesntNotify()
    {
        var data = new JObject
        {
            ["action"] = "edited",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["issue"] = new JObject
            {
                ["number"] = 2,
            },
            ["comment"] = new JObject
            {
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
                ["body"] = $"Something pizza {WatchedTeam}",
            },
            ["changes"] = new JObject
            {
                ["body"] = new JObject
                {
                    ["from"] = "Something pizza",
                },
            },
        };
        var eventName = "issue_comment";
        await SendWebHook(data, eventName, false);
    }

    [Test]
    public async Task EditedIssueCommentWithExistingMentionDoesntNotify()
    {
        var data = new JObject
        {
            ["action"] = "edited",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["issue"] = new JObject
            {
                ["number"] = 2,
            },
            ["comment"] = new JObject
            {
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
                ["body"] = $"Something pizza @{WatchedTeam}",
            },
            ["changes"] = new JObject
            {
                ["body"] = new JObject
                {
                    ["from"] = $"Something pizza @{WatchedTeam}",
                },
            },
        };
        var eventName = "issue_comment";
        await SendWebHook(data, eventName, false);
    }

    [Test]
    public async Task EditedIssueCommentWithRemovedMentionDoesntNotify()
    {
        var data = new JObject
        {
            ["action"] = "edited",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["issue"] = new JObject
            {
                ["number"] = 2,
            },
            ["comment"] = new JObject
            {
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
                ["body"] = $"Something pizza",
            },
            ["changes"] = new JObject
            {
                ["body"] = new JObject
                {
                    ["from"] = $"Something pizza @{WatchedTeam}",
                },
            },
        };
        var eventName = "issue_comment";
        await SendWebHook(data, eventName, false);
    }

    [Test]
    public async Task EditedIssueCommentWithNoMentionDoesntNotify()
    {
        var data = new JObject
        {
            ["action"] = "edited",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["issue"] = new JObject
            {
                ["number"] = 2,
            },
            ["comment"] = new JObject
            {
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
                ["body"] = "Something pizza",
            },
            ["changes"] = new JObject
            {
                ["body"] = new JObject
                {
                    ["from"] = $"Something pizza",
                },
            },
        };
        var eventName = "issue_comment";
        await SendWebHook(data, eventName, false);
    }

    [Test]
    public async Task NewPullRequestReviewCommentWithMentionNotifies()
    {
        var data = new JObject
        {
            ["action"] = "created",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["pull_request"] = new JObject
            {
                ["number"] = 2,
            },
            ["comment"] = new JObject
            {
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
                ["body"] = $"Something pizza @{WatchedTeam}",
            },
        };
        var eventName = "pull_request_review_comment";
        await SendWebHook(data, eventName, true);
    }

    [Test]
    public async Task NewPullRequestReviewCommentWithoutMentionDoesntNotify()
    {
        var data = new JObject
        {
            ["action"] = "created",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["pull_request"] = new JObject
            {
                ["number"] = 2,
            },
            ["comment"] = new JObject
            {
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
                ["body"] = $"Something pizza",
            },
        };
        var eventName = "pull_request_review_comment";
        await SendWebHook(data, eventName, false);
    }

    [Test]
    public async Task EditedPullRequestReviewCommentWithNewMentionNotifies()
    {
        var data = new JObject
        {
            ["action"] = "edited",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["pull_request"] = new JObject
            {
                ["number"] = 2,
            },
            ["comment"] = new JObject
            {
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
                ["body"] = $"Something pizza @{WatchedTeam}",
            },
            ["changes"] = new JObject
            {
                ["body"] = new JObject
                {
                    ["from"] = "Something pizza",
                },
            },
        };
        var eventName = "pull_request_review_comment";
        await SendWebHook(data, eventName, true);
    }

    [Test]
    public async Task EditedPullRequestReviewCommentWithExistingMentionDoesntNotify()
    {
        var data = new JObject
        {
            ["action"] = "edited",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["pull_request"] = new JObject
            {
                ["number"] = 2,
            },
            ["comment"] = new JObject
            {
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
                ["body"] = $"Something pizza @{WatchedTeam}",
            },
            ["changes"] = new JObject
            {
                ["body"] = new JObject
                {
                    ["from"] = $"Something pizza @{WatchedTeam}",
                },
            },
        };
        var eventName = "pull_request_review_comment";
        await SendWebHook(data, eventName, false);
    }

    [Test]
    public async Task EditedPullRequestReviewCommentWithRemovedMentionDoesntNotify()
    {
        var data = new JObject
        {
            ["action"] = "edited",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["pull_request"] = new JObject
            {
                ["number"] = 2,
            },
            ["comment"] = new JObject
            {
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
                ["body"] = $"Something pizza",
            },
            ["changes"] = new JObject
            {
                ["body"] = new JObject
                {
                    ["from"] = $"Something pizza @{WatchedTeam}",
                },
            },
        };
        var eventName = "pull_request_review_comment";
        await SendWebHook(data, eventName, false);
    }

    [Test]
    public async Task EditedPullRequestReviewCommentWithNoMentionDoesntNotify()
    {
        var data = new JObject
        {
            ["action"] = "edited",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "test-user",
                },
                ["name"] = "test",
            },
            ["pull_request"] = new JObject
            {
                ["number"] = 2,
            },
            ["comment"] = new JObject
            {
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
                ["body"] = "Something pizza",
            },
            ["changes"] = new JObject
            {
                ["body"] = new JObject
                {
                    ["from"] = $"Something pizza",
                },
            },
        };
        var eventName = "pull_request_review_comment";
        await SendWebHook(data, eventName, false);
    }

    #endregion

    #region Milestone Management Tests

    private Mock<IMilestonesClient> _mockGitHubMilestoneClient;
    private Mock<IIssuesLabelsClient> _mockGitHubLabelClient;
    private Mock<IIssueCommentsClient> _mockGitHubCommentClient;
    private Mock<IIssuesClient> _mockGitHubIssueClient;
    private Mock<IGitHubClient> _mockGitHubClient;
    private Mock<IGitHubApplicationClientFactory> _mockGitHubApplicationClientFactory;

    [Test]
    public async Task AddEpicLabelToIssue()
    {
        TestGitHubJson factory = new TestGitHubJson();
        var data = factory.WithActionLabeled("Epic")
            .WithRepository("test-user", "test")
            .WithIssue(title: "Epic Issue")
            .Build();

        var eventName = "issues";
        await SendWebHook(data, eventName, false, new List<Milestone>());

        _mockGitHubMilestoneClient.Verify(m => m.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<NewMilestone>()), Times.Once());
    }

    [Test]
    public async Task AddNonEpicLabelToIssue()
    {
        TestGitHubJson factory = new TestGitHubJson();
        var data = factory.WithActionLabeled("different label")
            .WithRepository("test-user", "test")
            .WithIssue(title: "Epic Issue")
            .Build();

        var eventName = "issues";
        await SendWebHook(data, eventName, false, new List<Milestone>());

        _mockGitHubMilestoneClient.Verify(m => m.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<NewMilestone>()), Times.Never());
    }

    [Test]
    public async Task EditEpicIssueTitleAndUpdateMilestoneName()
    {
        TestGitHubJson factory = new TestGitHubJson();
        var data = factory.WithActionEdited(previousTitle: "Epic Issue With a Old Name")
            .WithRepository("test-user", "test")
            .WithIssue(title: "Epic Issue With a New Name",
                milestoneName: "Epic Issue With a Old Name",
                openIssuesInMilestone: 5,
                labels: new string[] { "Epic" })
            .Build();

        var eventName = "issues";
        await SendWebHook(data, eventName, false, new List<Milestone>() { new Milestone(url: "", htmlUrl: "", id: 50, number: 50, nodeId: "", state: ItemState.Open, title: "Epic Issue With a Old Name", description: "", creator: new User(), openIssues: 1, closedIssues: 0, createdAt: DateTimeOffset.Now, dueOn: null, closedAt: null, updatedAt: null) });

        _mockGitHubMilestoneClient.Verify(m => m.Update(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<MilestoneUpdate>()), Times.Once());
    }

    [Test]
    public async Task EditEpicIssueTitleAndMilestoneNameNotTheSame()
    {
        TestGitHubJson factory = new TestGitHubJson();
        var data = factory.WithActionEdited(previousTitle: "Epic Issue With a Old Name")
            .WithRepository("test-user", "test")
            .WithIssue(title: "Epic Issue With a New Name",
                milestoneName: "Random Milestone Name",
                openIssuesInMilestone: 5,
                labels: new string[] { "Epic" })
            .Build();

        var eventName = "issues";
        await SendWebHook(data, eventName, false, new List<Milestone>() { new Milestone(url: "", htmlUrl: "", id: 50, number: 50, nodeId: "", state: ItemState.Open, title: "Random Milestone Name", description: "", creator: new User(), openIssues: 1, closedIssues: 0, createdAt: DateTimeOffset.Now, dueOn: null, closedAt: null, updatedAt: null) });

        _mockGitHubMilestoneClient.Verify(m => m.Update(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<MilestoneUpdate>()), Times.Once());
    }

    [Test]
    public async Task EditLegacyEpicIssueTitleThatHasNoMilestone()
    {
        TestGitHubJson factory = new TestGitHubJson();
        var data = factory.WithActionEdited(previousTitle: "Epic Issue With a Old Name")
            .WithRepository("test-user", "test")
            .WithIssue(title: "Epic Issue With a New Name",
                overrideNoMilestone: true,
                labels: new string[] { "Epic" })
            .Build();

        var eventName = "issues";
        await SendWebHook(data, eventName, false, new List<Milestone>());

        _mockGitHubMilestoneClient.Verify(m => m.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<NewMilestone>()), Times.Once());
    }

    [Test]
    public async Task AttemptToCloseEpicWithOpenIssuesInMilestone()
    {
        TestGitHubJson factory = new TestGitHubJson();
        var data = factory.WithAction("closed")
            .WithRepository("test-user", "test")
            .WithIssue(title: "Epic Issue With a New Name",
                milestoneName: "Epic Issue With a New Name",
                openIssuesInMilestone: 5,
                labels: new string[] { "Epic" })
            .Build();

        var eventName = "issues";
        await SendWebHook(data, eventName, false, new List<Milestone>() { new Milestone(url: "", htmlUrl: "", id: 50, number: 50, nodeId: "", state: ItemState.Open, title: "Epic Issue With a New Name", description: "", creator: new User(), openIssues: 5, closedIssues: 0, createdAt: DateTimeOffset.Now, dueOn: null, closedAt: null, updatedAt: null) });

        _mockGitHubIssueClient.Verify(m => m.Update(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IssueUpdate>()), Times.Once());
        _mockGitHubCommentClient.Verify(m => m.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Once());
    }

    [Test]
    public async Task AttemptToCloseEpicWithNoOpenIssuesInMilestone()
    {
        TestGitHubJson factory = new TestGitHubJson();
        var data = factory.WithAction("closed")
            .WithRepository("test-user", "test")
            .WithIssue(title: "Epic Issue With a New Name",
                milestoneName: "Epic Issue With a New Name",
                labels: new string[] { "Epic" })
            .Build();

        var eventName = "issues";
        await SendWebHook(data, eventName, false, new List<Milestone>() { new Milestone(url: "", htmlUrl: "", id: 50, number: 50, nodeId: "", state: ItemState.Open, title: "Epic Issue With a New Name", description: "", creator: new User(), openIssues: 0, closedIssues: 0, createdAt: DateTimeOffset.Now, dueOn: null, closedAt: null, updatedAt: null) });

        _mockGitHubIssueClient.Verify(m => m.Update(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IssueUpdate>()), Times.Never());
        _mockGitHubCommentClient.Verify(m => m.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never());
        _mockGitHubMilestoneClient.Verify(m => m.Update(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<MilestoneUpdate>()), Times.Once());
    }

    [Test]
    public async Task AttemptToCloseLegacyEpicIssueThatHasNoMilestone()
    {
        TestGitHubJson factory = new TestGitHubJson();
        var data = factory.WithAction("closed")
            .WithRepository("test-user", "test")
            .WithIssue(title: "Epic Issue With a New Name",
                overrideNoMilestone: true,
                labels: new string[] { "Epic" })
            .Build();

        var eventName = "issues";
        await SendWebHook(data, eventName, false, new List<Milestone>());

        _mockGitHubIssueClient.Verify(m => m.Update(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IssueUpdate>()), Times.Never());
        _mockGitHubCommentClient.Verify(m => m.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never());
        _mockGitHubMilestoneClient.Verify(m => m.Update(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<MilestoneUpdate>()), Times.Never());
    }

    [Test]
    public async Task AttemptToRemoveEpicLabelFromIssueWithOpenIssuesInMilestone()
    {
        TestGitHubJson factory = new TestGitHubJson();
        var data = factory.WithActionUnlabeled("Epic")
            .WithRepository("test-user", "test")
            .WithIssue(title: "Epic Issue With a New Name",
                milestoneName: "Epic Issue With a New Name",
                openIssuesInMilestone: 5,
                labels: new string[] { "Epic" })
            .Build();

        var eventName = "issues";
        await SendWebHook(data, eventName, false, new List<Milestone>() { new Milestone(url: "", htmlUrl: "", id: 50, number: 50, nodeId: "", state: ItemState.Open, title: "Epic Issue With a New Name", description: "", creator: new User(), openIssues: 5, closedIssues: 0, createdAt: DateTimeOffset.Now, dueOn: null, closedAt: null, updatedAt: null) });

        _mockGitHubLabelClient.Verify(m => m.AddToIssue(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string[]>()), Times.Once());
        _mockGitHubCommentClient.Verify(m => m.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Once());
    }

    [Test]
    public async Task AttemptToRemoveEpicLabelFromIssueWithNoOpenIssuesInMilestone()
    {        
        TestGitHubJson factory = new TestGitHubJson();
        var data = factory.WithActionUnlabeled("Epic")
            .WithRepository("test-user", "test")
            .WithIssue(title: "Epic Issue With a New Name",
                milestoneName: "Epic Issue With a New Name",
                openIssuesInMilestone: 1, // I know the test says no open issues, but the epic issue is technically an open issue in the epic, we just want to make sure nothing else is in there
                labels: new string[] { "Epic" })
            .Build();

        var eventName = "issues";
        await SendWebHook(data, eventName, false, new List<Milestone>() { new Milestone(url: "", htmlUrl: "", id: 50, number: 50, nodeId: "", state: ItemState.Open, title: "Epic Issue With a New Name", description: "", creator: new User(), openIssues: 1, closedIssues: 0, createdAt: DateTimeOffset.Now, dueOn: null, closedAt: null, updatedAt: null) });

        _mockGitHubLabelClient.Verify(m => m.AddToIssue(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string[]>()), Times.Never());
        _mockGitHubCommentClient.Verify(m => m.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never());
        _mockGitHubMilestoneClient.Verify(m => m.Update(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<MilestoneUpdate>()), Times.Once());
    }

    [Test]
    public async Task ReopenClosedEpicIssue()
    {
        TestGitHubJson factory = new TestGitHubJson();
        var data = factory.WithAction("reopened")
            .WithRepository("test-user", "test")
            .WithIssue(title: "Epic Issue With a New Name", 
                milestoneName: "Epic Issue With a New Name", 
                milestoneState: "closed", 
                labels: new string[] { "Epic" })
            .Build();

        var eventName = "issues";
        await SendWebHook(data, eventName, false, new List<Milestone>() { new Milestone(url: "", htmlUrl: "", id: 50, number: 50, nodeId: "", state: ItemState.Closed, title: "Epic Issue With a New Name", description: "", creator: new User(), openIssues: 0, closedIssues: 0, createdAt: DateTimeOffset.Now, dueOn: DateTimeOffset.Now, closedAt: DateTimeOffset.Now, updatedAt: DateTimeOffset.Now) });

        _mockGitHubMilestoneClient.Verify(m => m.Update(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<MilestoneUpdate>()), Times.Once());
    }

    [Test]
    public async Task NotAllowableRepoAddsEpicLabelToIssue()
    {
        TestGitHubJson factory = new TestGitHubJson();
        var data = factory.WithActionLabeled("Epic")
            .WithRepository("not-allowed", "test")
            .WithIssue(title: "Epic Issue")
            .Build();

        var eventName = "issues";
        await SendWebHook(data, eventName, false, new List<Milestone>());

        _mockGitHubMilestoneClient.Verify(m => m.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<NewMilestone>()), Times.Never());
    }

    #endregion

    private async Task SendWebHook(JObject data, string eventName, bool expectNotification,
        IReadOnlyList<Milestone> returnedMilestones = null)
    {
        using TestData testData = SetupTestData(expectNotification, returnedMilestones);
        var text = data.ToString();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/incoming/github")
        {
            Content = new StringContent(data.ToString(), Encoding.UTF8)
            {
                Headers =
                {
                    ContentType = new MediaTypeHeaderValue("application/json"),
                    ContentLength = text.Length,
                },
            },
            Headers =
            {
                {"X-GitHub-Event", eventName},
            },
        };
        var response = await testData.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        testData.VerifyAll();
    }

    public TestData SetupTestData(bool expectNotification,
        IReadOnlyList<Milestone> returnedMilestones = null)
    {
        var mockClientFactory = new MockHttpClientFactory();
        var factory = new TestAppFactory<DotNetStatusEmptyTestStartup>();

        _mockGitHubMilestoneClient = new Mock<IMilestonesClient>();
        _mockGitHubMilestoneClient.Setup(o => o.GetAllForRepository(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MilestoneRequest>()))
            .ReturnsAsync(returnedMilestones);
        _mockGitHubMilestoneClient.Setup(o => o.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<NewMilestone>()))
            .ReturnsAsync(new Milestone(number: 1));
        _mockGitHubMilestoneClient.Setup(o => o.Update(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<MilestoneUpdate>()))
            .ReturnsAsync(new Milestone(number: 1));

        _mockGitHubLabelClient = new Mock<IIssuesLabelsClient>();
        _mockGitHubLabelClient.Setup(o => o.AddToIssue(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string[]>()))
            .ReturnsAsync(new List<Label>());

        _mockGitHubCommentClient = new Mock<IIssueCommentsClient>();
        _mockGitHubCommentClient.Setup(o => o.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new IssueComment());

        _mockGitHubIssueClient = new Mock<IIssuesClient>();
        _mockGitHubIssueClient.Setup(o => o.Milestone).Returns(_mockGitHubMilestoneClient.Object);
        _mockGitHubIssueClient.Setup(o => o.Labels).Returns(_mockGitHubLabelClient.Object);
        _mockGitHubIssueClient.Setup(o => o.Comment).Returns(_mockGitHubCommentClient.Object);
        _mockGitHubIssueClient.Setup(o => o.Update(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IssueUpdate>()))
            .ReturnsAsync(new Issue());

        _mockGitHubClient = new Mock<IGitHubClient>();
        _mockGitHubClient.Setup(o => o.Issue).Returns(_mockGitHubIssueClient.Object);

        _mockGitHubApplicationClientFactory = new Mock<IGitHubApplicationClientFactory>();
        _mockGitHubApplicationClientFactory.Setup(o => o.CreateGitHubClientAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(_mockGitHubClient.Object);

        factory.ConfigureServices(services =>
        {
            services.AddControllers()
                .AddApplicationPart(typeof(GitHubHookController).Assembly)
                .AddGitHubWebHooks();
            services.Configure<TeamMentionForwardingOptions>(o =>
            {
                o.IgnoreRepos = new []{IgnoredRepo};
                o.WatchedTeam = WatchedTeam;
                o.TeamsWebHookUri = TestTeamsWebHookUri;
            });
            services.AddScoped<ITeamMentionForwarder, TeamMentionForwarder>();
            services.AddSingleton<Microsoft.Extensions.Internal.ISystemClock, TestClock>();
            services.AddLogging();
            services.AddSingleton<IHttpClientFactory>(mockClientFactory);

            services.AddSingleton(_mockGitHubApplicationClientFactory.Object);
            services.AddSingleton<IClientFactory<IAzureDevOpsClient>>(provider =>
                new SingleClientFactory<IAzureDevOpsClient>(Mock.Of<IAzureDevOpsClient>()));
            services.AddSingleton(Mock.Of<ITimelineIssueTriage>());


            services.RemoveAll<GitHubVerifySignatureFilter>();
            services.AddSingleton<TestVerifySignatureFilter>();
            services.Configure<MvcOptions>(o =>
            {
                o.Filters.Remove(o.Filters.OfType<ServiceFilterAttribute>()
                    .First(f => f.ServiceType == typeof(GitHubVerifySignatureFilter)));
                o.Filters.AddService<TestVerifySignatureFilter>();
            });
            services.AddSingleton(ExponentialRetry.Default);
            services.Configure<MilestoneManagementOptions>(o =>
            {
                o.ReposEnabledFor = new List<string> { "test-user/test" };
            });
        });
        factory.ConfigureBuilder(app =>
        {
            app.Use(async (context, next) =>
            {
                await next();
            });
            app.UseRouting();
            app.UseEndpoints(e => e.MapControllers());
        });
            
        if (expectNotification)
        {
            mockClientFactory.AddCannedResponse(TestTeamsWebHookUri, null, HttpStatusCode.NoContent, null, HttpMethod.Post);
        }

        return new TestData(factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://example.test", UriKind.Absolute),
            AllowAutoRedirect = false,
        }), factory, mockClientFactory);
    }

    public class TestData : IDisposable
    {
        public TestData(HttpClient client, 
            TestAppFactory<DotNetStatusEmptyTestStartup> factory, 
            MockHttpClientFactory mockClientFactory)
        {
            Client = client;
            Factory = factory;
            MockClientFactory = mockClientFactory;
        }

        public HttpClient Client { get; }
        public TestAppFactory<DotNetStatusEmptyTestStartup> Factory { get; }
        public MockHttpClientFactory MockClientFactory { get; }

        public void VerifyAll()
        {
            MockClientFactory.VerifyAll();
        }

        public void Dispose()
        {
            Client?.Dispose();
            Factory?.Dispose();
        }
    }

    public class TestGitHubJson : IDisposable
    {
        private JObject _returnObject;

        public TestGitHubJson()
        {
            _returnObject = new JObject();
        }

        public TestGitHubJson WithAction(string action)
        {
            _returnObject["action"] = action;
            return this;
        }

        /// <summary>
        /// Adds the labeled action to the json and the label that was added
        /// </summary>
        /// <param name="labelName">Name of the label applied to the issue</param>
        /// <returns></returns>
        public TestGitHubJson WithActionLabeled(string labelName)
        {
            _returnObject["action"] = "labeled";
            _returnObject["label"] = new JObject
            {
                ["name"] = labelName
            };
            return this;
        }

        /// <summary>
        /// Adds the unlabeled action to the json the label that was removed
        /// </summary>
        /// <param name="labelName">Name of the label removed from the issue</param>
        /// <returns></returns>
        public TestGitHubJson WithActionUnlabeled(string labelName)
        {
            _returnObject["action"] = "unlabeled";
            _returnObject["label"] = new JObject
            {
                ["name"] = labelName
            };
            return this;
        }

        public TestGitHubJson WithActionEdited(string previousTitle = null)
        {
            _returnObject["action"] = "edited";
            _returnObject["changes"] = new JObject
            {
                ["title"] = new JObject
                {
                    ["from"] = previousTitle
                }
            };
            return this;
        }

        public TestGitHubJson WithRepository(string owner, string repo)
        {
            _returnObject["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = owner,
                },
                ["name"] = repo,
            };
            return this;
        }

        public TestGitHubJson WithIssue(string title = "",
            string milestoneName = "Milestone Name",
            string milestoneState = "open",
            int openIssuesInMilestone = 0,
            bool overrideNoMilestone = false,
            string[] labels = null)
        {
            JArray labelsArray = new JArray();

            if (labels != null)
            {
                for (int id = 1; id <= labels.Length; id++)
                {
                    labelsArray.Add(new JObject
                    {
                        ["id"] = id,
                        ["name"] = labels[id-1]
                    }); ;
                }
            }

            _returnObject["issue"] = new JObject
            {
                ["number"] = 2,
                ["body"] = "Something pizza",
                ["user"] = new JObject
                {
                    ["login"] = "thatguy",
                },
                ["html_url"] = "https://FAKE-GITHUB-URL/test-user/test",
                ["title"] = title,
                ["milestone"] = overrideNoMilestone ? null : new JObject
                {
                    ["url"] = "",
                    ["htmlUrl"] = "",
                    ["id"] = 50,
                    ["number"] = 50,
                    ["nodeId"] = "",
                    ["state"] = milestoneState,
                    ["title"] = milestoneName,
                    ["description"] = "",
                    ["creator"] = null,
                    ["open_issues"] = openIssuesInMilestone,
                    ["closed_issues"] = 0,
                    ["created_at"] = "2023-07-12T12:34:56Z",
                    ["due_on"] = "2023-07-12T12:34:56Z",
                    ["closed_at"] = "2023-07-12T12:34:56Z",
                    ["updated_at"] = "2023-07-12T12:34:56Z"
                },
                ["labels"] = labelsArray
            };
            return this;
        }

        public JObject Build()
        {
            return _returnObject;
        }

        public void Dispose()
        {
            _returnObject = null;
        }
    }
}
