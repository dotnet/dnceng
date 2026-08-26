using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new Request(
                request.Method,
                request.RequestUri.AbsolutePath,
                request.Headers.TryGetValues("X-Disable-Provenance", out IEnumerable<string> values)
                    ? values.Single()
                    : null));
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed record Request(HttpMethod Method, string Path, string DisableProvenance);
}
