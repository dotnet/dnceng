// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DncEng.GrafanaWatchdog.Tests.TestSupport;

/// <summary>
/// A <see cref="HttpMessageHandler"/> test double that returns a scripted sequence of responses (or
/// exceptions) per exact request URI, so a single handler instance can back several workspace/endpoint
/// combinations in the same test.
/// </summary>
internal sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
    public delegate Task<HttpResponseMessage> Responder(HttpRequestMessage request, CancellationToken cancellationToken);

    private readonly Dictionary<string, Queue<Responder>> _routes = new();

    public List<HttpRequestMessage> Requests { get; } = new();

    public void Enqueue(string absoluteUri, Responder responder)
    {
        if (!_routes.TryGetValue(absoluteUri, out Queue<Responder>? queue))
        {
            queue = new Queue<Responder>();
            _routes[absoluteUri] = queue;
        }

        queue.Enqueue(responder);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        string key = request.RequestUri!.ToString();

        if (!_routes.TryGetValue(key, out Queue<Responder>? queue) || queue.Count == 0)
        {
            throw new InvalidOperationException($"No scripted response is configured for '{key}'.");
        }

        Responder responder = queue.Dequeue();
        return await responder(request, cancellationToken).ConfigureAwait(false);
    }

    public static Responder Respond(HttpStatusCode statusCode, string? json = null) =>
        (_, _) =>
        {
            var response = new HttpResponseMessage(statusCode);
            if (json is not null)
            {
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            return Task.FromResult(response);
        };

    public static Responder RespondAfterDelay(TimeSpan delay, HttpStatusCode statusCode, string json) =>
        async (_, cancellationToken) =>
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        };

    public static Responder Throw(Exception exception) =>
        (_, _) => throw exception;
}
