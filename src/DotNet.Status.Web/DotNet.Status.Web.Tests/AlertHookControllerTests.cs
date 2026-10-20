using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using DotNet.Status.Web.Controllers;
using DotNet.Status.Web.Models;
using DotNet.Status.Web.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.DotNet.Internal.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace DotNet.Status.Web.Tests;

[TestFixture]
public class AlertHookControllerTests
{
    [Test]
    public void GenerateDescription_WithMissingEvalMatches_DoesNotThrow()
    {
        AlertHookController controller = CreateController();
        GrafanaNotification notification = new GrafanaNotification
        {
            Title = "Alert title",
            State = "alerting",
            Message = "Something went wrong",
            RuleUrl = "https://example/rule",
            RuleId = 1,
            EvalMatches = null,
        };

        Action action = () => controller.GenerateDescription(notification);

        action.Should().NotThrow();

        string description = controller.GenerateDescription(notification);
        description.Should().Contain("Supplemental text");
        description.Should().Contain("Grafana-Automated-Alert-Id-");
    }

    [Test]
    public void GenerateComment_WithMissingEvalMatches_DoesNotThrow()
    {
        AlertHookController controller = CreateController();
        GrafanaNotification notification = new GrafanaNotification
        {
            Title = "Alert title",
            State = "alerting",
            Message = "Something went wrong",
            RuleUrl = "https://example/rule",
            EvalMatches = null,
        };

        Action action = () => controller.GenerateComment(notification);

        action.Should().NotThrow();

        string comment = controller.GenerateComment(notification);
        comment.Should().Contain("Metric state changed to");
        comment.Should().Contain("alerting");
    }

    [Test]
    public void GenerateTitle_WithPrefix_PrependsPrefixToTitle()
    {
        AlertHookController controller = CreateController();
        GrafanaNotification notification = new GrafanaNotification
        {
            Title = "CPU High",
            State = "alerting",
        };

        string title = controller.GenerateTitle(notification);

        title.Should().Be("[test] CPU High");
    }

    [Test]
    public void GenerateDescription_WithEvalMatches_IncludesMetrics()
    {
        AlertHookController controller = CreateController();
        GrafanaNotification notification = new GrafanaNotification
        {
            Title = "Alert title",
            State = "alerting",
            Message = "High CPU",
            RuleUrl = "https://example/rule",
            RuleId = 1,
            EvalMatches = new List<GrafanaNotificationMatch>
            {
                new GrafanaNotificationMatch { Metric = "cpu_usage", Value = 95.5 },
            }.ToImmutableList(),
        };

        string description = controller.GenerateDescription(notification);

        description.Should().Contain("cpu_usage");
        description.Should().Contain("95.5");
    }

    [Test]
    public void GetUniqueIdentifier_WithLegacyTags_UsesNotificationId()
    {
        GrafanaNotification notification = new GrafanaNotification
        {
            RuleId = 1,
            Tags = ImmutableDictionary<string, string>.Empty
                .Add(AlertHookController.NotificationTagName, "legacy-notification"),
        };

        string identifier = AlertHookController.GetUniqueIdentifier(notification);

        identifier.Should().Be("legacy-notification");
    }

    [Test]
    public void GetUniqueIdentifier_WithUnifiedAlertLabels_UsesNotificationId()
    {
        GrafanaNotification notification = new GrafanaNotification
        {
            CommonLabels = ImmutableDictionary<string, string>.Empty
                .Add(AlertHookController.NotificationTagName, "unified-notification"),
        };

        string identifier = AlertHookController.GetUniqueIdentifier(notification);

        identifier.Should().Be("unified-notification");
    }

    [Test]
    public void GetUniqueIdentifier_WithoutNotificationId_UsesRuleUid()
    {
        GrafanaNotification notification = new GrafanaNotification
        {
            CommonLabels = ImmutableDictionary<string, string>.Empty
                .Add(AlertHookController.RuleUidLabelName, "rule-uid"),
        };

        string identifier = AlertHookController.GetUniqueIdentifier(notification);

        identifier.Should().Be("rule-uid");
    }

    [Test]
    public void GetUniqueIdentifier_WithAlertLabels_UsesNotificationId()
    {
        GrafanaNotification notification = new GrafanaNotification
        {
            Alerts = new List<GrafanaAlert>
            {
                new GrafanaAlert
                {
                    Labels = ImmutableDictionary<string, string>.Empty
                        .Add(AlertHookController.NotificationTagName, "nested-notification"),
                },
            }.ToImmutableList(),
        };

        string identifier = AlertHookController.GetUniqueIdentifier(notification);

        identifier.Should().Be("nested-notification");
    }

    [Test]
    public void GetUniqueIdentifier_WithZeroRuleIdAndNoLabels_Throws()
    {
        GrafanaNotification notification = new GrafanaNotification();

        Action action = () => AlertHookController.GetUniqueIdentifier(notification);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*must provide*");
    }

    [Test]
    public void GetUniqueIdentifier_WithConflictingNotificationIds_Throws()
    {
        GrafanaNotification notification = new GrafanaNotification
        {
            CommonLabels = ImmutableDictionary<string, string>.Empty
                .Add(AlertHookController.NotificationTagName, "first-notification"),
            Alerts = new List<GrafanaAlert>
            {
                new GrafanaAlert
                {
                    Labels = ImmutableDictionary<string, string>.Empty
                        .Add(AlertHookController.NotificationTagName, "second-notification"),
                },
            }.ToImmutableList(),
        };

        Action action = () => AlertHookController.GetUniqueIdentifier(notification);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*conflicting 'NotificationId' values*");
    }

    [Test]
    public void GetUniqueIdentifier_WithMixedAlertIdentifiers_Throws()
    {
        GrafanaNotification notification = new GrafanaNotification
        {
            Alerts = new List<GrafanaAlert>
            {
                new GrafanaAlert
                {
                    Labels = ImmutableDictionary<string, string>.Empty
                        .Add(AlertHookController.NotificationTagName, "notification-id"),
                },
                new GrafanaAlert
                {
                    Labels = ImmutableDictionary<string, string>.Empty
                        .Add(AlertHookController.RuleUidLabelName, "rule-uid"),
                },
            }.ToImmutableList(),
        };

        Action action = () => AlertHookController.GetUniqueIdentifier(notification);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*conflicting stable identifiers*");
    }

    [TestCase("alerting", false)]
    [TestCase("no_data", false)]
    [TestCase("alerting", true)]
    [TestCase("no_data", true)]
    public async Task NotifyAsync_NewAlert_CreatesTask(string state, bool useConfiguredOptions)
    {
        AzureDevOpsAlertOptions options = useConfiguredOptions
            ? new ConfigurationBuilder()
                .SetBasePath(TestContext.CurrentContext.TestDirectory)
                .AddJsonFile(".config/settings.json")
                .Build()
                .GetSection("AzureDevOpsAlert")
                .Get<AzureDevOpsAlertOptions>()
            : null;
        Mock<IAzureDevOpsClient> client = new(MockBehavior.Strict);
        client.Setup(c => c.QueryWorkItemsAsync("internal", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorkItem>());
        client.Setup(c => c.CreateWorkItemAsync(
                "internal", "Task", It.IsAny<Dictionary<string, string>>(),
                @"internal\.NET Engineering Services\First Responders", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkItem { Id = 42 });
        AlertHookController controller = CreateController(client, options);
        GrafanaNotification notification = new()
        {
            Title = "CPU High",
            RuleId = 1,
            RuleUrl = "https://example/rule",
            State = state,
        };

        IActionResult result = await controller.NotifyAsync(notification);

        result.Should().BeOfType<NoContentResult>();
        client.Verify(c => c.CreateWorkItemAsync(
            "internal", "Task",
            It.Is<Dictionary<string, string>>(fields =>
                fields["System.Title"].EndsWith("CPU High") &&
                fields["System.Description"].Contains("<strong>" + state + "</strong>") &&
                fields["System.Description"].Contains("https://example/rule") &&
                fields["System.Description"].Contains("Grafana-Automated-Alert-Id-1") &&
                fields["System.State"] == "Backlog" &&
                fields["System.Tags"] == "Grafana Alert; Active Alert"),
            @"internal\.NET Engineering Services\First Responders", It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.QueryWorkItemsAsync(
            "internal", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        client.VerifyNoOtherCalls();
    }

    [TestCase("Task", "alerting", "Done")]
    [TestCase("DNCENG Task", "alerting", "Done")]
    [TestCase("Task", "no_data", "Backlog")]
    [TestCase("DNCENG Task", "no_data", "Backlog")]
    [TestCase("Task", "ok", "Backlog")]
    [TestCase("DNCENG Task", "ok", "Backlog")]
    public async Task NotifyAsync_ExistingWorkItem_PreservesTypeAndUpdatesLifecycle(
        string type, string state, string previousState)
    {
        bool recovering = state == "ok";
        string expectedState = recovering ? "Done" : "Backlog";
        string expectedTag = recovering ? "Inactive Alert" : "Active Alert";
        string removedTag = recovering ? "Active Alert" : "Inactive Alert";
        WorkItem existing = new()
        {
            Id = 42,
            Fields = new Dictionary<string, object>
            {
                ["System.WorkItemType"] = type,
                ["System.State"] = previousState,
                ["System.Tags"] = $"Grafana Alert; {removedTag}; Retained tag",
            },
        };
        string query = null;
        Mock<IAzureDevOpsClient> client = new(MockBehavior.Strict);
        client.Setup(c => c.QueryWorkItemsAsync("internal", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, wiql, _) => query = wiql)
            .ReturnsAsync(new[] { existing });
        client.Setup(c => c.UpdateWorkItemAsync(
                "internal", 42, It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        client.Setup(c => c.AddWorkItemCommentAsync("internal", 42, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        AlertHookController controller = CreateController(client);

        IActionResult result = await controller.NotifyAsync(new GrafanaNotification
        {
            RuleId = 1,
            RuleUrl = "https://example/rule",
            State = state,
        });

        result.Should().BeOfType<NoContentResult>();
        query.Should().Contain("[System.TeamProject] = 'internal'")
            .And.Contain(@"[System.AreaPath] UNDER 'internal\.NET Engineering Services\First Responders'")
            .And.Contain("Grafana-Automated-Alert-Id-1")
            .And.Contain("[System.Tags] CONTAINS 'Grafana Alert'")
            .And.NotContain("[System.WorkItemType]")
            .And.NotContain("[System.State]");
        client.Verify(c => c.UpdateWorkItemAsync("internal", 42,
            It.Is<Dictionary<string, string>>(fields =>
                fields.Count == 2 &&
                fields["System.State"] == expectedState &&
                fields["System.Tags"].Contains(expectedTag) &&
                fields["System.Tags"].Contains("Retained tag") &&
                !fields["System.Tags"].Contains(removedTag)), It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.AddWorkItemCommentAsync("internal", 42,
            It.Is<string>(comment => comment.Contains("<strong>" + state + "</strong>")),
            It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.CreateWorkItemAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static AlertHookController CreateController(
        Mock<IAzureDevOpsClient> azureDevOpsClient = null, AzureDevOpsAlertOptions options = null)
    {
        azureDevOpsClient ??= new(MockBehavior.Strict);
        var clientFactory = new SingleClientFactory<IAzureDevOpsClient>(azureDevOpsClient.Object);
        IOptions<AzureDevOpsAlertOptions> alertOptions = Microsoft.Extensions.Options.Options.Create(options ?? new AzureDevOpsAlertOptions
        {
            Organization = "dnceng",
            Project = "internal",
            AreaPath = @"internal\.NET Engineering Services\First Responders",
            TitlePrefix = "[test] ",
            SupplementalBodyText = "Supplemental text",
        });

        IOptions<GrafanaOptions> grafanaOptions = Microsoft.Extensions.Options.Options.Create(new GrafanaOptions
        {
            WebhookSecret = "test-secret",
        });

        var controller = new AlertHookController(
            clientFactory,
            alertOptions,
            grafanaOptions,
            NullLogger<AlertHookController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        controller.Request.Headers.Authorization =
            "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("grafana:test-secret"));
        return controller;
    }
}
