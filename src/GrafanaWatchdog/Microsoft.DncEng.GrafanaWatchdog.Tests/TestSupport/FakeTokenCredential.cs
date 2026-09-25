// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;

namespace Microsoft.DncEng.GrafanaWatchdog.Tests.TestSupport;

/// <summary>
/// A <see cref="TokenCredential"/> test double that records the requested scopes and either returns
/// a fixed token or invokes a caller-supplied factory (e.g. to simulate a failure).
/// </summary>
internal sealed class FakeTokenCredential : TokenCredential
{
    private readonly Func<TokenRequestContext, AccessToken> _tokenFactory;
    private int _callCount;

    public FakeTokenCredential(string token)
        : this(_ => new AccessToken(token, DateTimeOffset.UtcNow.AddHours(1)))
    {
    }

    public FakeTokenCredential(Func<TokenRequestContext, AccessToken> tokenFactory)
    {
        _tokenFactory = tokenFactory;
    }

    public string[]? LastRequestedScopes { get; private set; }

    public int GetTokenCallCount => _callCount;

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        LastRequestedScopes = requestContext.Scopes;
        return _tokenFactory(requestContext);
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        return new ValueTask<AccessToken>(GetToken(requestContext, cancellationToken));
    }
}
