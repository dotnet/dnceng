// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Azure.Core;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NUnit.Framework;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Internal.AzureDevOps.Tests;

[TestFixture]
public class AzureDevOpsClientAuthTests
{
    private ILogger<AzureDevOpsClient> _logger =
        LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<AzureDevOpsClient>();

    /// <summary>
    /// When AccessToken is configured, requests should use Basic authentication
    /// with the PAT encoded as base64(":token").
    /// </summary>
    [Test]
    public async Task Client_WithAccessToken_UsesBasicAuth()
    {
        // Arrange
        const string pat = "test-pat-value";
        var expectedBasic = Convert.ToBase64String(Encoding.UTF8.GetBytes($":{pat}"));

        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(new { count = 0, value = Array.Empty<object>() }),
                Encoding.UTF8,
                "application/json")
        });
        var factory = new DelegatingHandlerHttpClientFactory(handler);

        var options = new AzureDevOpsClientOptions
        {
            Organization = "test-org",
            AccessToken = pat,
            MaxParallelRequests = 1,
        };

        var client = new AzureDevOpsClient(options, _logger, factory, tokenCredential: null);

        // Act
        await client.ListBuilds("test-project", CancellationToken.None);

        // Assert
        Assert.That(handler.LastRequest, Is.Not.Null, "Expected at least one request to be captured");
        Assert.That(handler.LastRequest!.Headers.Authorization, Is.Not.Null);
        Assert.That(handler.LastRequest.Headers.Authorization!.Scheme, Is.EqualTo("Basic"));
        Assert.That(handler.LastRequest.Headers.Authorization.Parameter, Is.EqualTo(expectedBasic));
    }

    /// <summary>
    /// When UseManagedIdentity is true with a client ID (user-assigned MI), requests
    /// should use Bearer authentication with a token obtained from the TokenCredential.
    /// </summary>
    [Test]
    public async Task Client_WithUserAssignedManagedIdentity_UsesBearerAuth()
    {
        // Arrange
        const string fakeToken = "fake-entra-bearer-token";

        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(new { count = 0, value = Array.Empty<object>() }),
                Encoding.UTF8,
                "application/json")
        });
        var factory = new DelegatingHandlerHttpClientFactory(handler);

        var mockCredential = new FakeTokenCredential(fakeToken);

        var options = new AzureDevOpsClientOptions
        {
            Organization = "test-org",
            UseManagedIdentity = true,
            ManagedIdentityClientId = "00000000-0000-0000-0000-000000000001",
            MaxParallelRequests = 1,
        };

        var client = new AzureDevOpsClient(options, _logger, factory, tokenCredential: mockCredential);

        // Act
        await client.ListBuilds("test-project", CancellationToken.None);

        // Assert
        Assert.That(handler.LastRequest, Is.Not.Null, "Expected at least one request to be captured");
        Assert.That(handler.LastRequest!.Headers.Authorization, Is.Not.Null);
        Assert.That(handler.LastRequest.Headers.Authorization!.Scheme, Is.EqualTo("Bearer"));
        Assert.That(handler.LastRequest.Headers.Authorization.Parameter, Is.EqualTo(fakeToken));
    }

    /// <summary>
    /// When UseManagedIdentity is true without a client ID (system-assigned MI), requests
    /// should use Bearer authentication with a token obtained from the TokenCredential.
    /// </summary>
    [Test]
    public async Task Client_WithSystemAssignedManagedIdentity_UsesBearerAuth()
    {
        // Arrange
        const string fakeToken = "fake-system-assigned-token";

        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(new { count = 0, value = Array.Empty<object>() }),
                Encoding.UTF8,
                "application/json")
        });
        var factory = new DelegatingHandlerHttpClientFactory(handler);

        var mockCredential = new FakeTokenCredential(fakeToken);

        var options = new AzureDevOpsClientOptions
        {
            Organization = "test-org",
            UseManagedIdentity = true,
            MaxParallelRequests = 1,
        };

        var client = new AzureDevOpsClient(options, _logger, factory, tokenCredential: mockCredential);

        // Act
        await client.ListBuilds("test-project", CancellationToken.None);

        // Assert
        Assert.That(handler.LastRequest, Is.Not.Null, "Expected at least one request to be captured");
        Assert.That(handler.LastRequest!.Headers.Authorization, Is.Not.Null);
        Assert.That(handler.LastRequest.Headers.Authorization!.Scheme, Is.EqualTo("Bearer"));
        Assert.That(handler.LastRequest.Headers.Authorization.Parameter, Is.EqualTo(fakeToken));
    }

    /// <summary>
    /// When UseManagedIdentity is configured, the client should request a token
    /// for the Azure DevOps resource scope.
    /// </summary>
    [Test]
    public async Task Client_WithManagedIdentity_RequestsCorrectScope()
    {
        // Arrange
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(new { count = 0, value = Array.Empty<object>() }),
                Encoding.UTF8,
                "application/json")
        });
        var factory = new DelegatingHandlerHttpClientFactory(handler);

        var mockCredential = new FakeTokenCredential("token");

        var options = new AzureDevOpsClientOptions
        {
            Organization = "test-org",
            UseManagedIdentity = true,
            MaxParallelRequests = 1,
        };

        var client = new AzureDevOpsClient(options, _logger, factory, tokenCredential: mockCredential);

        // Act
        await client.ListBuilds("test-project", CancellationToken.None);

        // Assert
        Assert.That(mockCredential.LastRequestedScopes, Is.Not.Null);
        Assert.That(mockCredential.LastRequestedScopes, Does.Contain(AzureDevOpsClient.AzureDevOpsResourceId));
    }

    /// <summary>
    /// AccessToken takes precedence over UseManagedIdentity if both are set.
    /// </summary>
    [Test]
    public async Task Client_WithBothPatAndManagedIdentity_PrefersPatAuth()
    {
        // Arrange
        const string pat = "test-pat-value";
        var expectedBasic = Convert.ToBase64String(Encoding.UTF8.GetBytes($":{pat}"));

        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(new { count = 0, value = Array.Empty<object>() }),
                Encoding.UTF8,
                "application/json")
        });
        var factory = new DelegatingHandlerHttpClientFactory(handler);

        var mockCredential = new FakeTokenCredential("should-not-be-used");

        var options = new AzureDevOpsClientOptions
        {
            Organization = "test-org",
            AccessToken = pat,
            UseManagedIdentity = true,
            ManagedIdentityClientId = "00000000-0000-0000-0000-000000000001",
            MaxParallelRequests = 1,
        };

        var client = new AzureDevOpsClient(options, _logger, factory, tokenCredential: mockCredential);

        // Act
        await client.ListBuilds("test-project", CancellationToken.None);

        // Assert
        Assert.That(handler.LastRequest!.Headers.Authorization!.Scheme, Is.EqualTo("Basic"));
        Assert.That(handler.LastRequest.Headers.Authorization.Parameter, Is.EqualTo(expectedBasic));
        Assert.That(mockCredential.GetTokenCallCount, Is.EqualTo(0),
            "Token credential should not be called when AccessToken is provided");
    }

    /// <summary>
    /// When neither AccessToken nor UseManagedIdentity is set, no auth header
    /// should be present on requests.
    /// </summary>
    [Test]
    public async Task Client_WithNoAuth_SendsNoAuthHeader()
    {
        // Arrange
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(new { count = 0, value = Array.Empty<object>() }),
                Encoding.UTF8,
                "application/json")
        });
        var factory = new DelegatingHandlerHttpClientFactory(handler);

        var options = new AzureDevOpsClientOptions
        {
            Organization = "test-org",
            MaxParallelRequests = 1,
        };

        var client = new AzureDevOpsClient(options, _logger, factory, tokenCredential: null);

        // Act
        await client.ListBuilds("test-project", CancellationToken.None);

        // Assert
        Assert.That(handler.LastRequest!.Headers.Authorization, Is.Null);
    }

    /// <summary>
    /// Token is refreshed on each request to handle token expiry.
    /// </summary>
    [Test]
    public async Task Client_WithManagedIdentity_RefreshesTokenPerRequest()
    {
        // Arrange
        var handler = new CapturingHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(new { count = 0, value = Array.Empty<object>() }),
                Encoding.UTF8,
                "application/json")
        });
        var factory = new DelegatingHandlerHttpClientFactory(handler);

        var mockCredential = new FakeTokenCredential("token");

        var options = new AzureDevOpsClientOptions
        {
            Organization = "test-org",
            UseManagedIdentity = true,
            MaxParallelRequests = 1,
        };

        var client = new AzureDevOpsClient(options, _logger, factory, tokenCredential: mockCredential);

        // Act - make two requests
        await client.ListBuilds("test-project", CancellationToken.None);
        await client.ListBuilds("test-project", CancellationToken.None);

        // Assert - token should have been requested at least twice
        Assert.That(mockCredential.GetTokenCallCount, Is.GreaterThanOrEqualTo(2));
    }

    #region Test helpers

    /// <summary>
    /// A fake <see cref="TokenCredential"/> that returns a predetermined token
    /// and records the requested scopes.
    /// </summary>
    private sealed class FakeTokenCredential : TokenCredential
    {
        private readonly string _token;
        private int _callCount;

        public FakeTokenCredential(string token)
        {
            _token = token;
        }

        public string[]? LastRequestedScopes { get; private set; }
        public int GetTokenCallCount => _callCount;

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            LastRequestedScopes = requestContext.Scopes;
            return new AccessToken(_token, DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            LastRequestedScopes = requestContext.Scopes;
            return new ValueTask<AccessToken>(new AccessToken(_token, DateTimeOffset.UtcNow.AddHours(1)));
        }
    }

    /// <summary>
    /// A <see cref="DelegatingHandler"/> that captures the last request seen and
    /// always returns a preconfigured response.
    /// </summary>
    private sealed class CapturingHandler : DelegatingHandler
    {
        private readonly Func<HttpResponseMessage> _responseFactory;

        public CapturingHandler(HttpResponseMessage cannedResponse)
            : this(() => cannedResponse)
        {
        }

        public CapturingHandler(Func<HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
            InnerHandler = new HttpClientHandler();
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_responseFactory());
        }
    }

    /// <summary>
    /// An <see cref="IHttpClientFactory"/> implementation that returns a client
    /// backed by a specific <see cref="DelegatingHandler"/>.
    /// </summary>
    private sealed class DelegatingHandlerHttpClientFactory : IHttpClientFactory
    {
        private readonly DelegatingHandler _handler;

        public DelegatingHandlerHttpClientFactory(DelegatingHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name) => new HttpClient(_handler, disposeHandler: false);
    }

    #endregion
}
