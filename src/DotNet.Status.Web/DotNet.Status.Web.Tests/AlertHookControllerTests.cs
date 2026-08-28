using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using AwesomeAssertions;
using DotNet.Status.Web.Controllers;
using DotNet.Status.Web.Models;
using DotNet.Status.Web.Options;
using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.DotNet.Internal.DependencyInjection;
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

    private static AlertHookController CreateController()
    {
        Mock<IAzureDevOpsClient> azureDevOpsClient = new(MockBehavior.Strict);
        Mock<IClientFactory<IAzureDevOpsClient>> clientFactory = new(MockBehavior.Strict);
        IOptions<AzureDevOpsAlertOptions> alertOptions = Microsoft.Extensions.Options.Options.Create(new AzureDevOpsAlertOptions
        {
            Organization = "dnceng",
            Project = "internal",
            AreaPath = @"internal\.NET Engineering Services\First Responders",
            WorkItemType = "DNCENG Task",
            TitlePrefix = "[test] ",
            SupplementalBodyText = "Supplemental text",
        });

        IOptions<GrafanaOptions> grafanaOptions = Microsoft.Extensions.Options.Options.Create(new GrafanaOptions
        {
            WebhookSecret = "test-secret",
        });

        return new AlertHookController(
            clientFactory.Object,
            alertOptions,
            grafanaOptions,
            NullLogger<AlertHookController>.Instance);
    }
}
