using System;
using System.Reflection;
using AwesomeAssertions;
using DotNet.Status.Web.Controllers;
using DotNet.Status.Web.Models;
using DotNet.Status.Web.Options;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Octokit;

namespace DotNet.Status.Web.Tests;

[TestFixture]
public class AlertHookControllerTests
{
    [Test]
    public void GenerateNewIssue_WithMissingEvalMatchesAndNotificationTargets_DoesNotThrow()
    {
        AlertHookController controller = CreateController(null);
        GrafanaNotification notification = new GrafanaNotification
        {
            Title = "Alert title",
            State = "alerting",
            Message = "Something went wrong",
            RuleUrl = "https://example/rule",
            EvalMatches = null,
        };

        Action action = () => InvokeGenerateNewIssue(controller, notification);

        action.Should().NotThrow();

        NewIssue issue = InvokeGenerateNewIssue(controller, notification);
        issue.Body.Should().Contain("Please investigate");
        issue.Body.Should().NotContain(", please investigate");
    }

    [Test]
    public void GenerateNewNotificationComment_WithMissingEvalMatches_DoesNotThrow()
    {
        AlertHookController controller = CreateController(Array.Empty<string>());
        GrafanaNotification notification = new GrafanaNotification
        {
            Title = "Alert title",
            State = "alerting",
            Message = "Something went wrong",
            RuleUrl = "https://example/rule",
            EvalMatches = null,
        };

        Action action = () => InvokeGenerateNewNotificationComment(controller, notification);

        action.Should().NotThrow();

        string comment = InvokeGenerateNewNotificationComment(controller, notification);
        comment.Should().Contain("Metric state changed to *alerting*");
    }

    private static AlertHookController CreateController(string[] notificationTargets)
    {
        Mock<IGitHubTokenProvider> tokenProvider = new(MockBehavior.Strict);
        IOptions<GitHubConnectionOptions> githubOptions = Microsoft.Extensions.Options.Options.Create(new GitHubConnectionOptions
        {
            Organization = "dotnet",
            Repository = "dnceng",
            NotificationTargets = notificationTargets,
            AlertLabels = Array.Empty<string>(),
            EnvironmentLabels = Array.Empty<string>(),
            TitlePrefix = "[test] ",
            SupplementalBodyText = "Supplemental text",
        });
        IOptions<GitHubClientOptions> clientOptions = Microsoft.Extensions.Options.Options.Create(new GitHubClientOptions
        {
            ProductHeader = new ProductHeaderValue("DotNetStatusWebTests"),
        });

        return new AlertHookController(
            tokenProvider.Object,
            githubOptions,
            clientOptions,
            NullLogger<AlertHookController>.Instance);
    }

    private static NewIssue InvokeGenerateNewIssue(AlertHookController controller, GrafanaNotification notification)
    {
        MethodInfo method = typeof(AlertHookController).GetMethod("GenerateNewIssue", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        return (NewIssue)method.Invoke(controller, new object[] { notification });
    }

    private static string InvokeGenerateNewNotificationComment(AlertHookController controller, GrafanaNotification notification)
    {
        MethodInfo method = typeof(AlertHookController).GetMethod("GenerateNewNotificationComment", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        return (string)method.Invoke(controller, new object[] { notification });
    }
}
