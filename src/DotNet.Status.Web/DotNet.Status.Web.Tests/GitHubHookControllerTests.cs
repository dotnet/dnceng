using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Policy;
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

    [Test]
    public async Task AddEpicLabelToIssue()
    {
        var data = new JObject
        {
            ["action"] = "labeled",
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
                ["html_url"] = "https://FAKE-GITHUB-URL/test-user/test",
                ["title"] = "Epic Issue"
            },
            ["label"] = new JObject
            {
                ["name"] = "Epic"
            }
        };
        var eventName = "issues";
        await SendWebHook(data, eventName, false, new List<Milestone>());
    }

    [Test]
    public async Task AddNonEpicLabelToIssue()
    {
        var data = new JObject
        {
            ["action"] = "labeled",
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
                ["html_url"] = "https://FAKE-GITHUB-URL/test-user/test",
                ["title"] = "Epic Issue"
            },
            ["label"] = new JObject
            {
                ["name"] = "different label"
            }
        };
        var eventName = "issues";
        await SendWebHook(data, eventName, false, new List<Milestone>());
    }

    [Test]
    public async Task EditEpicIssueTitleAndUpdateMilestoneName()
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
                ["html_url"] = "https://FAKE-GITHUB-URL/test-user/test",
                ["title"] = "Epic Issue With a New Name",
                ["milestone"] = new JObject
                {
                    ["url"] = "",
                    ["htmlUrl"] = "",
                    ["id"] = 50,
                    ["number"] = 50,
                    ["nodeId"] = "",
                    ["state"] = "open",
                    ["title"] = "Epic Issue With a Old Name",
                    ["description"] = "",
                    ["creator"] = null,
                    ["open_issues"] = 5,
                    ["closed_issues"] = 0,
                    ["created_at"] = "2023-07-12T12:34:56Z",
                    ["due_on"] = "2023-07-12T12:34:56Z",
                    ["closed_at"] = null,
                    ["updated_at"] = null
                },
                ["labels"] = new JArray
                {
                    new JObject
                    {
                        ["id"] = 1,
                        ["name"] = "Epic"
                    }
                }
            },
            ["changes"] = new JObject
            {
                ["title"] = new JObject
                {
                    ["from"] = "Epic Issue With a Old Name"
                }
            }
        };
        var eventName = "issues";
        await SendWebHook(data, eventName, false, new List<Milestone>() { new Milestone(url: "", htmlUrl: "", id: 50, number: 50, nodeId: "", state: ItemState.Open, title: "Epic Issue With a Old Name", description: "", creator: new User(), openIssues: 1, closedIssues: 0, createdAt: DateTimeOffset.Now, dueOn: null, closedAt: null, updatedAt: null) });
    }

    [Test]
    public async Task EditEpicIssueTitleAndMilestoneNameNotTheSame()
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
                ["html_url"] = "https://FAKE-GITHUB-URL/test-user/test",
                ["title"] = "Epic Issue With a New Name",
                ["milestone"] = new JObject
                {
                    ["url"] = "",
                    ["htmlUrl"] = "",
                    ["id"] = 50,
                    ["number"] = 50,
                    ["nodeId"] = "",
                    ["state"] = "open",
                    ["title"] = "Random Milestone Name",
                    ["description"] = "",
                    ["creator"] = null,
                    ["open_issues"] = 5,
                    ["closed_issues"] = 0,
                    ["created_at"] = "2023-07-12T12:34:56Z",
                    ["due_on"] = "2023-07-12T12:34:56Z",
                    ["closed_at"] = null,
                    ["updated_at"] = null
                },
                ["labels"] = new JArray
                {
                    new JObject
                    {
                        ["id"] = 1,
                        ["name"] = "Epic"
                    }
                }
            },
            ["changes"] = new JObject
            {
                ["title"] = new JObject
                {
                    ["from"] = "Epic Issue With a Old Name"
                }
            }
        };
        var eventName = "issues";
        await SendWebHook(data, eventName, false, new List<Milestone>() { new Milestone(url: "", htmlUrl: "", id: 50, number: 50, nodeId: "", state: ItemState.Open, title: "Random Milestone Name", description: "", creator: new User(), openIssues: 1, closedIssues: 0, createdAt: DateTimeOffset.Now, dueOn: null, closedAt: null, updatedAt: null) });
    }

    [Test]
    public async Task EditLegacyEpicIssueTitleThatHasNoMilestone()
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
                ["html_url"] = "https://FAKE-GITHUB-URL/test-user/test",
                ["title"] = "Epic Issue With a New Name",
                ["labels"] = new JArray
                {
                    new JObject
                    {
                        ["id"] = 1,
                        ["name"] = "Epic"
                    }
                }
            },
            ["changes"] = new JObject
            {
                ["title"] = new JObject
                {
                    ["from"] = "Epic Issue With a Old Name"
                }
            }
        };
        var eventName = "issues";
        await SendWebHook(data, eventName, false, new List<Milestone>());
    }

    [Test]
    public async Task AttemptToCloseEpicWithOpenIssuesInMilestone()
    {
        var data = new JObject
        {
            ["action"] = "closed",
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
                ["html_url"] = "https://FAKE-GITHUB-URL/test-user/test",
                ["title"] = "Epic Issue With a New Name",
                ["milestone"] = new JObject
                {
                    ["url"] = "", 
                    ["htmlUrl"] = "", 
                    ["id"] = 50, 
                    ["number"] = 50, 
                    ["nodeId"] = "", 
                    ["state"] = "open", 
                    ["title"] = "Epic Issue With a New Name", 
                    ["description"] = "", 
                    ["creator"] = null, 
                    ["open_issues"] = 5, 
                    ["closed_issues"] = 0, 
                    ["created_at"] = "2023-07-12T12:34:56Z", 
                    ["due_on"] = "2023-07-12T12:34:56Z", 
                    ["closed_at"] = null, 
                    ["updated_at"] = null
                },
                ["labels"] = new JArray
                    {
                        new JObject
                        {
                            ["id"] = 1,
                            ["name"] = "Epic"
                        }
                    }
                }
            };
        var eventName = "issues";
        await SendWebHook(data, eventName, false, new List<Milestone>() { new Milestone(url: "", htmlUrl: "", id: 50, number: 50, nodeId: "", state: ItemState.Open, title: "Epic Issue With a New Name", description: "", creator: new User(), openIssues: 5, closedIssues: 0, createdAt: DateTimeOffset.Now, dueOn: null, closedAt: null, updatedAt: null) });
    }

    [Test]
    public async Task AttemptToCloseEpicWithNoOpenIssuesInMilestone()
    {
        var data = new JObject
        {
            ["action"] = "closed",
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
                ["html_url"] = "https://FAKE-GITHUB-URL/test-user/test",
                ["title"] = "Epic Issue With a New Name",
                ["milestone"] = new JObject
                {
                    ["url"] = "",
                    ["htmlUrl"] = "",
                    ["id"] = 50,
                    ["number"] = 50,
                    ["nodeId"] = "",
                    ["state"] = "open",
                    ["title"] = "Epic Issue With a New Name",
                    ["description"] = "",
                    ["creator"] = null,
                    ["open_issues"] = 0,
                    ["closed_issues"] = 0,
                    ["created_at"] = "2023-07-12T12:34:56Z",
                    ["due_on"] = "2023-07-12T12:34:56Z",
                    ["closed_at"] = null,
                    ["updated_at"] = null
                },
                ["labels"] = new JArray
                    {
                        new JObject
                        {
                            ["id"] = 1,
                            ["name"] = "Epic"
                        }
                    }
            }
        };
        var eventName = "issues";
        await SendWebHook(data, eventName, false, new List<Milestone>() { new Milestone(url: "", htmlUrl: "", id: 50, number: 50, nodeId: "", state: ItemState.Open, title: "Epic Issue With a New Name", description: "", creator: new User(), openIssues: 0, closedIssues: 0, createdAt: DateTimeOffset.Now, dueOn: null, closedAt: null, updatedAt: null) });
    }

    [Test]
    public async Task AttemptToCloseLegacyEpicIssueThatHasNoMilestone()
    {
        var data = new JObject
        {
            ["action"] = "closed",
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
                ["html_url"] = "https://FAKE-GITHUB-URL/test-user/test",
                ["title"] = "Epic Issue With a New Name",
                ["labels"] = new JArray
                {
                    new JObject
                    {
                        ["id"] = 1,
                        ["name"] = "Epic"
                    }
                }
            },
        };
        var eventName = "issues";
        await SendWebHook(data, eventName, false, new List<Milestone>());
    }

    [Test]
    public async Task AttemptToRemoveEpicLabelFromIssueWithOpenIssuesInMilestone()
    {
        var data = new JObject
        {
            ["action"] = "unlabeled",
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
                ["html_url"] = "https://FAKE-GITHUB-URL/test-user/test",
                ["title"] = "Epic Issue With a New Name",
                ["milestone"] = new JObject
                {
                    ["url"] = "",
                    ["htmlUrl"] = "",
                    ["id"] = 50,
                    ["number"] = 50,
                    ["nodeId"] = "",
                    ["state"] = "open",
                    ["title"] = "Epic Issue With a New Name",
                    ["description"] = "",
                    ["creator"] = null,
                    ["open_issues"] = 5,
                    ["closed_issues"] = 0,
                    ["created_at"] = "2023-07-12T12:34:56Z",
                    ["due_on"] = "2023-07-12T12:34:56Z",
                    ["closed_at"] = null,
                    ["updated_at"] = null
                },
            },
            ["label"] = new JObject
            {
                ["name"] = "Epic"
            }
        };
        var eventName = "issues";
        await SendWebHook(data, eventName, false, new List<Milestone>() { new Milestone(url: "", htmlUrl: "", id: 50, number: 50, nodeId: "", state: ItemState.Open, title: "Epic Issue With a New Name", description: "", creator: new User(), openIssues: 5, closedIssues: 0, createdAt: DateTimeOffset.Now, dueOn: null, closedAt: null, updatedAt: null) });
    }

    [Test]
    public async Task AttemptToRemoveEpicLabelFromIssueWithNoOpenIssuesInMilestone()
    {
        var data = new JObject
        {
            ["action"] = "unlabeled",
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
                ["html_url"] = "https://FAKE-GITHUB-URL/test-user/test",
                ["title"] = "Epic Issue With a New Name",
                ["milestone"] = new JObject
                {
                    ["url"] = "",
                    ["htmlUrl"] = "",
                    ["id"] = 50,
                    ["number"] = 50,
                    ["nodeId"] = "",
                    ["state"] = "open",
                    ["title"] = "Epic Issue With a New Name",
                    ["description"] = "",
                    ["creator"] = null,
                    ["open_issues"] = 0,
                    ["closed_issues"] = 0,
                    ["created_at"] = "2023-07-12T12:34:56Z",
                    ["due_on"] = "2023-07-12T12:34:56Z",
                    ["closed_at"] = null,
                    ["updated_at"] = null
                }
            },
            ["label"] = new JObject
            {
                ["name"] = "Epic"
            }
        };
        var eventName = "issues";
        await SendWebHook(data, eventName, false, new List<Milestone>() { new Milestone(url: "", htmlUrl: "", id: 50, number: 50, nodeId: "", state: ItemState.Open, title: "Epic Issue With a New Name", description: "", creator: new User(), openIssues: 0, closedIssues: 0, createdAt: DateTimeOffset.Now, dueOn: null, closedAt: null, updatedAt: null) });
    }

    [Test]
    public async Task ReopenClosedEpicIssue()
    {
        var data = new JObject
        {
            ["action"] = "reopened",
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
                ["html_url"] = "https://FAKE-GITHUB-URL/test-user/test",
                ["title"] = "Epic Issue With a New Name",
                ["milestone"] = new JObject
                {
                    ["url"] = "",
                    ["htmlUrl"] = "",
                    ["id"] = 50,
                    ["number"] = 50,
                    ["nodeId"] = "",
                    ["state"] = "closed",
                    ["title"] = "Epic Issue With a New Name",
                    ["description"] = "",
                    ["creator"] = null,
                    ["open_issues"] = 0,
                    ["closed_issues"] = 0,
                    ["created_at"] = "2023-07-12T12:34:56Z",
                    ["due_on"] = "2023-07-12T12:34:56Z",
                    ["closed_at"] = "2023-07-12T12:34:56Z",
                    ["updated_at"] = "2023-07-12T12:34:56Z"
                },
                ["labels"] = new JArray
                    {
                        new JObject
                        {
                            ["id"] = 1,
                            ["name"] = "Epic"
                        }
                    }
            }
        };
        var eventName = "issues";
        await SendWebHook(data, eventName, false, new List<Milestone>() { new Milestone(url: "", htmlUrl: "", id: 50, number: 50, nodeId: "", state: ItemState.Closed, title: "Epic Issue With a New Name", description: "", creator: new User(), openIssues: 0, closedIssues: 0, createdAt: DateTimeOffset.Now, dueOn: DateTimeOffset.Now, closedAt: DateTimeOffset.Now, updatedAt: DateTimeOffset.Now) });
    }

    [Test]
    public async Task NotAllowableRepoAddsEpicLabelToIssue()
    {
        var data = new JObject
        {
            ["action"] = "labeled",
            ["repository"] = new JObject
            {
                ["owner"] = new JObject
                {
                    ["login"] = "not-allowed",
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
                ["html_url"] = "https://FAKE-GITHUB-URL/test-user/test",
                ["title"] = "Epic Issue"
            },
            ["label"] = new JObject
            {
                ["name"] = "Epic"
            }
        };
        var eventName = "issues";
        await SendWebHook(data, eventName, false, new List<Milestone>());
    }

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
        IReadOnlyList<Milestone> returnedMilestones = null,
        IReadOnlyList<Label> returnLabels = null)
    {
        var mockClientFactory = new MockHttpClientFactory();
        var factory = new TestAppFactory<DotNetStatusEmptyTestStartup>();

        var mockGitHubMilestoneClient = new Mock<IMilestonesClient>();
        mockGitHubMilestoneClient.Setup(o => o.GetAllForRepository(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MilestoneRequest>()))
            .ReturnsAsync(returnedMilestones);
        mockGitHubMilestoneClient.Setup(o => o.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<NewMilestone>()))
            .ReturnsAsync(new Milestone(number: 1));
        mockGitHubMilestoneClient.Setup(o => o.Update(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<MilestoneUpdate>()))
            .ReturnsAsync(new Milestone(number: 1));

        var mockGitHubLabelClient = new Mock<IIssuesLabelsClient>();
        mockGitHubLabelClient.Setup(o => o.AddToIssue(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string[]>()))
            .ReturnsAsync(returnLabels);

        var mockGitHubIssueClient = new Mock<IIssuesClient>();
        mockGitHubIssueClient.Setup(o => o.Milestone).Returns(mockGitHubMilestoneClient.Object);
        mockGitHubIssueClient.Setup(o => o.Labels).Returns(mockGitHubLabelClient.Object);


        var mockGitHubClient = new Mock<IGitHubClient>();
        mockGitHubClient.Setup(o => o.Issue).Returns(mockGitHubIssueClient.Object);
        
        var mockGitHubApplicationClientFactory = new Mock<IGitHubApplicationClientFactory>();
        mockGitHubApplicationClientFactory.Setup(o => o.CreateGitHubClientAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(mockGitHubClient.Object);

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

            services.AddSingleton(mockGitHubApplicationClientFactory.Object);
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
        public TestData(HttpClient client, TestAppFactory<DotNetStatusEmptyTestStartup> factory, MockHttpClientFactory mockClientFactory)
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
}
