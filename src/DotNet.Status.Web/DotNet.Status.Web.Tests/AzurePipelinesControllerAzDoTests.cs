// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Status.Web.Controllers;
using DotNet.Status.Web.Options;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.DotNet.Internal.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DotNet.Status.Web.Tests;

[TestFixture]
public class AzurePipelinesControllerAzDoTests
{
    private static AzurePipelinesController.AzureDevOpsEvent<AzurePipelinesController.AzureDevOpsMinimalBuildResource> CreateBuildEvent()
    {
        return new AzurePipelinesController.AzureDevOpsEvent<AzurePipelinesController.AzureDevOpsMinimalBuildResource>
        {
            Resource = new AzurePipelinesController.AzureDevOpsMinimalBuildResource
            {
                Id = 999,
                Url = "test-build-url"
            },
            ResourceContainers = new AzurePipelinesController.AzureDevOpsResourceContainers
            {
                Collection = new AzurePipelinesController.HasId { Id = "collection-id" },
                Account = new AzurePipelinesController.HasId { Id = "account-id" },
                Project = new AzurePipelinesController.HasId { Id = "project-id" }
            }
        };
    }

    private static JObject CreateBuildJson(string definitionName = "test-pipeline", string branch = "refs/heads/main")
    {
        return new JObject
        {
            ["_links"] = new JObject { ["web"] = new JObject { ["href"] = "https://dev.azure.com/dnceng/internal/_build/results?buildId=999" } },
            ["buildNumber"] = "20260522.1",
            ["definition"] = new JObject { ["name"] = definitionName, ["path"] = "\\test" },
            ["finishTime"] = "05/22/2026 12:00:00",
            ["id"] = "999",
            ["project"] = new JObject { ["name"] = "internal" },
            ["reason"] = "schedule",
            ["requestedFor"] = new JObject { ["displayName"] = "Build Agent" },
            ["result"] = "failed",
            ["sourceBranch"] = branch,
            ["startTime"] = "05/22/2026 11:30:00",
        };
    }

    private static BuildMonitorOptions CreateOptionsWithAzDoTarget()
    {
        return new BuildMonitorOptions
        {
            Monitor = new BuildMonitorOptions.AzurePipelinesOptions
            {
                Organization = "dnceng",
                Builds = new[]
                {
                    new BuildMonitorOptions.AzurePipelinesOptions.BuildDescription
                    {
                        Project = "internal",
                        DefinitionPath = "\\test\\test-pipeline",
                        Branches = new[] { "main" },
                        IssuesId = "azdo-fr-target"
                    }
                }
            },
            Issues = new BuildMonitorOptions.IssuesOptions[0],
            AzDoIssues = new[]
            {
                new BuildMonitorOptions.AzDoIssuesOptions
                {
                    Id = "azdo-fr-target",
                    Project = "internal",
                    AreaPath = "internal\\.NET Engineering Services\\First Responders",
                    WorkItemType = "DNCENG Task",
                    Tags = new[] { "Ops - First Responder" },
                    UpdateExisting = true
                }
            }
        };
    }

    private (AzurePipelinesController controller, Mock<IAzureDevOpsClient> mockClient) CreateController(
        BuildMonitorOptions options, JObject buildJson)
    {
        var mockClient = new Mock<IAzureDevOpsClient>();
        mockClient.Setup(m => m.GetProjectNameAsync("project-id"))
            .Returns(Task.FromResult("internal"));

        if (buildJson != null)
        {
            var build = JsonConvert.DeserializeObject<Build>(buildJson.ToString());
            mockClient.Setup(m => m.GetBuildAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(build));
        }

        mockClient.Setup(m => m.GetBuildChangesAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(((BuildChange[] changes, int? truncatedChangeCount)?)(new BuildChange[0], 0)));
        mockClient.Setup(m => m.GetTimelineAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new Timeline()));

        mockClient.Setup(m => m.CreateWorkItemAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new WorkItem { Id = 12345 }));

        mockClient.Setup(m => m.QueryWorkItemsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new WorkItem[0]));

        mockClient.Setup(m => m.AddWorkItemCommentAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mockGithubClientFactory = new Mock<IGitHubApplicationClientFactory>();
        var clientFactory = new SingleClientFactory<IAzureDevOpsClient>(mockClient.Object);
        var optionsSnapshot = new Mock<IOptionsSnapshot<BuildMonitorOptions>>();
        optionsSnapshot.Setup(o => o.Value).Returns(options);

        var controller = new AzurePipelinesController(
            mockGithubClientFactory.Object,
            clientFactory,
            optionsSnapshot.Object,
            NullLogger<AzurePipelinesController>.Instance);

        return (controller, mockClient);
    }

    [Test]
    public async Task BuildComplete_AzDoTarget_CreatesNewWorkItem()
    {
        var options = CreateOptionsWithAzDoTarget();
        var buildJson = CreateBuildJson();
        var (controller, mockClient) = CreateController(options, buildJson);

        var result = await controller.BuildComplete(CreateBuildEvent());

        mockClient.Verify(m => m.CreateWorkItemAsync(
            "internal",
            "DNCENG Task",
            It.Is<Dictionary<string, string>>(d =>
                d["System.Title"].StartsWith("Build failed: test-pipeline/main") &&
                d["System.Description"].Contains("20260522.1") &&
                d["System.State"] == "Backlog" &&
                d["System.Tags"].Contains("Build Failed") &&
                d["System.Tags"].Contains("Ops - First Responder")),
            "internal\\.NET Engineering Services\\First Responders",
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task BuildComplete_AzDoTarget_UpdateExisting_AddsComment()
    {
        var options = CreateOptionsWithAzDoTarget();
        var buildJson = CreateBuildJson();
        var (controller, mockClient) = CreateController(options, buildJson);

        // Simulate existing work item found
        mockClient.Setup(m => m.QueryWorkItemsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new[] { new WorkItem { Id = 42 } }));

        var result = await controller.BuildComplete(CreateBuildEvent());

        // Should add comment, not create new work item
        mockClient.Verify(m => m.AddWorkItemCommentAsync(
            "internal", 42, It.Is<string>(s => s.Contains("20260522.1")), It.IsAny<CancellationToken>()),
            Times.Once);

        mockClient.Verify(m => m.CreateWorkItemAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task BuildComplete_AzDoTarget_UpdateExisting_NoMatch_CreatesNew()
    {
        var options = CreateOptionsWithAzDoTarget();
        var buildJson = CreateBuildJson();
        var (controller, mockClient) = CreateController(options, buildJson);

        // No existing work items found
        mockClient.Setup(m => m.QueryWorkItemsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new WorkItem[0]));

        var result = await controller.BuildComplete(CreateBuildEvent());

        mockClient.Verify(m => m.CreateWorkItemAsync(
            "internal", "DNCENG Task", It.IsAny<Dictionary<string, string>>(),
            "internal\\.NET Engineering Services\\First Responders",
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task BuildComplete_AzDoTarget_PrioritizedOverGitHub()
    {
        // If both a GitHub issue target and AzDo target exist with the same ID, AzDo wins
        var options = CreateOptionsWithAzDoTarget();
        options.Issues = new[]
        {
            new BuildMonitorOptions.IssuesOptions
            {
                Id = "azdo-fr-target",
                Owner = "dotnet",
                Name = "dnceng",
                Labels = new[] { "Build Failed" }
            }
        };

        var buildJson = CreateBuildJson();
        var (controller, mockClient) = CreateController(options, buildJson);

        var result = await controller.BuildComplete(CreateBuildEvent());

        // Should create AzDO work item (not GitHub issue)
        mockClient.Verify(m => m.CreateWorkItemAsync(
            "internal", "DNCENG Task", It.IsAny<Dictionary<string, string>>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task BuildComplete_GitHubTarget_StillWorks()
    {
        // When only GitHub target exists, it should still work (no AzDoIssues configured)
        var options = new BuildMonitorOptions
        {
            Monitor = new BuildMonitorOptions.AzurePipelinesOptions
            {
                Organization = "dnceng",
                Builds = new[]
                {
                    new BuildMonitorOptions.AzurePipelinesOptions.BuildDescription
                    {
                        Project = "internal",
                        DefinitionPath = "\\test\\test-pipeline",
                        Branches = new[] { "main" },
                        IssuesId = "github-target"
                    }
                }
            },
            Issues = new[]
            {
                new BuildMonitorOptions.IssuesOptions
                {
                    Id = "github-target",
                    Owner = "dotnet",
                    Name = "dnceng",
                    Labels = new[] { "Build Failed" }
                }
            },
            AzDoIssues = null
        };

        var buildJson = CreateBuildJson();
        var (controller, mockClient) = CreateController(options, buildJson);

        // Setup GitHub mocks
        var mockGithubClient = new Mock<Octokit.IGitHubClient>();
        var mockIssues = new Mock<Octokit.IIssuesClient>();
        var mockComments = new Mock<Octokit.IIssueCommentsClient>();
        mockGithubClient.SetupGet(m => m.Issue).Returns(mockIssues.Object);
        mockIssues.SetupGet(m => m.Comment).Returns(mockComments.Object);
        mockIssues.Setup(m => m.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Octokit.NewIssue>()))
            .Returns(Task.FromResult(new Octokit.Issue()));

        var mockFactory = new Mock<IGitHubApplicationClientFactory>();
        mockFactory.Setup(m => m.CreateGitHubClientAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.FromResult(mockGithubClient.Object));

        var clientFactory = new SingleClientFactory<IAzureDevOpsClient>(mockClient.Object);
        var optionsSnapshot = new Mock<IOptionsSnapshot<BuildMonitorOptions>>();
        optionsSnapshot.Setup(o => o.Value).Returns(options);

        var controllerWithGithub = new AzurePipelinesController(
            mockFactory.Object,
            clientFactory,
            optionsSnapshot.Object,
            NullLogger<AzurePipelinesController>.Instance);

        var result = await controllerWithGithub.BuildComplete(CreateBuildEvent());

        // Should NOT create AzDO work item
        mockClient.Verify(m => m.CreateWorkItemAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Should create GitHub issue
        mockIssues.Verify(m => m.Create("dotnet", "dnceng", It.IsAny<Octokit.NewIssue>()), Times.Once);
    }

    [Test]
    public async Task BuildComplete_NonMatchingBranch_NoWorkItemCreated()
    {
        var options = CreateOptionsWithAzDoTarget();
        var buildJson = CreateBuildJson(branch: "refs/heads/release/8.0");
        var (controller, mockClient) = CreateController(options, buildJson);

        var result = await controller.BuildComplete(CreateBuildEvent());

        mockClient.Verify(m => m.CreateWorkItemAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
