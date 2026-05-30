// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNet.Status.Web;

public class WebhookBasicAuthOptions : AuthenticationSchemeOptions
{
    public string SharedSecret { get; set; }
}

/// <summary>
/// Authentication handler that validates HTTP Basic Auth credentials against a shared secret.
/// Used for AzDO service hook webhook endpoints where the password is a pre-shared secret
/// stored in Key Vault. The username is ignored (AzDO sends "user" by default).
/// </summary>
public class WebhookBasicAuthHandler : AuthenticationHandler<WebhookBasicAuthOptions>
{
    public const string SchemeName = "webhook-basic";

    public WebhookBasicAuthHandler(
        IOptionsMonitor<WebhookBasicAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var secret = Options.SharedSecret;
        if (string.IsNullOrEmpty(secret))
        {
            Logger.LogError("WebhookBasicAuth SharedSecret is not configured; failing authentication");
            return Task.FromResult(AuthenticateResult.Fail("Webhook secret not configured"));
        }

        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing Authorization header"));
        }

        var headerValue = authHeader.ToString();
        if (!headerValue.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.Fail("Not a Basic auth header"));
        }

        try
        {
            var encoded = headerValue.Substring("Basic ".Length).Trim();
            var bytes = Convert.FromBase64String(encoded);

            try
            {
                int colon = Array.IndexOf(bytes, (byte)':');
                if (colon < 0)
                {
                    return Task.FromResult(AuthenticateResult.Fail("Invalid Basic auth format"));
                }

                var provided = bytes.AsSpan(colon + 1);
                var expected = Encoding.UTF8.GetBytes(secret);

                if (!CryptographicOperations.FixedTimeEquals(provided, expected))
                {
                    Logger.LogWarning("Webhook basic auth failed: invalid secret");
                    return Task.FromResult(AuthenticateResult.Fail("Invalid webhook secret"));
                }

                var identity = new ClaimsIdentity(SchemeName);
                identity.AddClaim(new Claim(ClaimTypes.Name, "azdo-service-hook"));
                identity.AddClaim(new Claim(ClaimTypes.AuthenticationMethod, "webhook-basic-auth"));
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, SchemeName);

                return Task.FromResult(AuthenticateResult.Success(ticket));
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }
        catch (FormatException)
        {
            return Task.FromResult(AuthenticateResult.Fail("Malformed Base64 in Authorization header"));
        }
    }
}
