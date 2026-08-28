using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Microsoft.DotNet.Monitoring.Sdk.Tests;

internal class GrafanaClientRetirementTests
{
    [Test]
    public async Task DeleteAlertRuleVerifiesDeletion()
    {
        var handler = new RecordingHandler(
            Json(HttpStatusCode.OK, "{}"),
            Json(HttpStatusCode.OK, "{}"),
            Json(HttpStatusCode.NotFound, "{}"));
        using var client = new GrafanaClient("https://grafana.example", new HttpClient(handler));

        bool deleted = await client.DeleteAlertRuleAsync("rule/one");

        deleted.Should().BeTrue();
        handler.Requests.Select(request => request.Method).Should()
            .Equal(HttpMethod.Get, HttpMethod.Delete, HttpMethod.Get);
        handler.Requests[1].Path.Should().Be("/api/v1/provisioning/alert-rules/rule%2Fone");
        handler.Requests[1].DisableProvenance.Should().Be("true");
    }

    [Test]
    public async Task DeleteAlertRuleReportsAlreadyAbsent()
    {
        var handler = new RecordingHandler(Json(HttpStatusCode.NotFound, "{}"));
        using var client = new GrafanaClient("https://grafana.example", new HttpClient(handler));

        bool deleted = await client.DeleteAlertRuleAsync("missing");

        deleted.Should().BeFalse();
        handler.Requests.Should().ContainSingle();
    }

    [Test]
    public async Task DeleteContactPointsDeletesEveryMatchingIntegration()
    {
        var handler = new RecordingHandler(
            Json(HttpStatusCode.OK, """
                [
                  { "name": "Retired", "uid": "one" },
                  { "name": "Retired", "uid": "two" },
                  { "name": "Keep", "uid": "three" }
                ]
                """),
            Json(HttpStatusCode.OK, "{}"),
            Json(HttpStatusCode.OK, "{}"),
            Json(HttpStatusCode.OK, """[{ "name": "Keep", "uid": "three" }]"""));
        using var client = new GrafanaClient("https://grafana.example", new HttpClient(handler));

        int deleted = await client.DeleteContactPointsByNameAsync("Retired");

        deleted.Should().Be(2);
        handler.Requests.Where(request => request.Method == HttpMethod.Delete)
            .Select(request => request.Path)
            .Should().Equal(
                "/api/v1/provisioning/contact-points/one",
                "/api/v1/provisioning/contact-points/two");
    }

    [Test]
    public async Task NotificationPolicyReferenceIsDetectedRecursively()
    {
        var handler = new RecordingHandler(Json(HttpStatusCode.OK, """
            {
              "receiver": "default",
              "routes": [
                {
                  "receiver": "Retired",
                  "routes": []
                }
              ]
            }
            """));
        using var client = new GrafanaClient("https://grafana.example", new HttpClient(handler));

        bool referenced = await client.NotificationPolicyReferencesContactPointAsync("Retired");

        referenced.Should().BeTrue();
    }

    [Test]
    public async Task SetAlertRuleGroupIntervalPreservesRulesAndVerifiesUpdate()
    {
        const string group = """
            {
              "title": "Data Migration Alerts",
              "folderUid": "dnceng",
              "interval": 60,
              "rules": [
                {
                  "uid": "data-migration-job-processing-time",
                  "title": "Data Migration Job Processing Time"
                }
              ]
            }
            """;
        const string updatedGroup = """
            {
              "title": "Data Migration Alerts",
              "folderUid": "dnceng",
              "interval": 300,
              "rules": [
                {
                  "uid": "data-migration-job-processing-time",
                  "title": "Data Migration Job Processing Time"
                }
              ]
            }
            """;
        var handler = new RecordingHandler(
            Json(HttpStatusCode.OK, group),
            Json(HttpStatusCode.Accepted, "{}"),
            Json(HttpStatusCode.OK, updatedGroup));
        using var client = new GrafanaClient("https://grafana.example", new HttpClient(handler));

        await client.SetAlertRuleGroupIntervalAsync("dnceng", "Data Migration Alerts", 300);

        handler.Requests.Select(request => request.Method).Should()
            .Equal(HttpMethod.Get, HttpMethod.Put, HttpMethod.Get);
        handler.Requests[1].Path.Should()
            .Be("/api/v1/provisioning/folder/dnceng/rule-groups/Data%20Migration%20Alerts");
        handler.Requests[1].DisableProvenance.Should().Be("true");
        JObject requestBody = JObject.Parse(handler.Requests[1].Body);
        requestBody.Value<int>("interval").Should().Be(300);
        requestBody.Value<JArray>("rules").Should().ContainSingle();
        requestBody.SelectToken("rules[0].uid").Value<string>().Should()
            .Be("data-migration-job-processing-time");
    }

    [Test]
    public async Task SetAlertRuleGroupIntervalRejectsDroppedRules()
    {
        const string group = """
            {
              "title": "Data Migration Alerts",
              "folderUid": "dnceng",
              "interval": 60,
              "rules": [
                {
                  "uid": "data-migration-job-processing-time",
                  "title": "Data Migration Job Processing Time"
                }
              ]
            }
            """;
        var handler = new RecordingHandler(
            Json(HttpStatusCode.OK, group),
            Json(HttpStatusCode.Accepted, "{}"),
            Json(HttpStatusCode.OK, """
                {
                  "title": "Data Migration Alerts",
                  "folderUid": "dnceng",
                  "interval": 300,
                  "rules": []
                }
                """));
        using var client = new GrafanaClient("https://grafana.example", new HttpClient(handler));

        Func<Task> update = () =>
            client.SetAlertRuleGroupIntervalAsync("dnceng", "Data Migration Alerts", 300);

        await update.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*changed its rule set*");
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string content) =>
        new(statusCode) { Content = new StringContent(content) };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public RecordingHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public List<Request> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
            Requests.Add(new Request(
                request.Method,
                request.RequestUri.AbsolutePath,
                request.Headers.TryGetValues("X-Disable-Provenance", out IEnumerable<string> values)
                    ? values.Single()
                    : null,
                body));
            return _responses.Dequeue();
        }
    }

    private sealed record Request(HttpMethod Method, string Path, string DisableProvenance, string Body);
}
