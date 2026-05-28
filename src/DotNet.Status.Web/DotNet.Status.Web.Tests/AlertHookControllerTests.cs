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
            EvalMatches = new List<GrafanaNotificationMatch>
            {
                new GrafanaNotificationMatch { Metric = "cpu_usage", Value = 95.5 },
            }.ToImmutableList(),
        };

        string description = controller.GenerateDescription(notification);

        description.Should().Contain("cpu_usage");
        description.Should().Contain("95.5");
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

