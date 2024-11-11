using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.DncEng.Configuration.Extensions;

namespace Microsoft.DncEng.SecretManager;

public sealed class SecretManagerCredentialProvider : ITokenCredentialProvider
{
    /// <inheritdoc/>
    public string ApplicationId { get; internal set; }
    /// <inheritdoc/>
    public string TenantId { get; internal set; }

    public SecretManagerCredentialProvider()
    {
        SetCredentialIdentityValues();
    }

    // Expect AzureCliCredential for CI and local dev environments. 
    // Use InteractiveBrowserCredential as a fall back for local dev environments.
    private readonly Lazy<TokenCredential> _credential = new(() =>
        new ChainedTokenCredential(
            new AzureCliCredential(new AzureCliCredentialOptions { TenantId = ConfigurationConstants.MsftAdTenantId }),
            new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions() { TenantId = ConfigurationConstants.MsftAdTenantId })
        ));
    public Task<TokenCredential> GetCredentialAsync()
    {
        return Task.FromResult(_credential.Value);
    }

    /// <inheritdoc/>
    internal void SetCredentialIdentityValues()
    {
        try
        {
            // Get a token from the credential provider
            var tokenRequestContext = new TokenRequestContext(new[] { "https://management.azure.com/.default" });
            var token = _credential.Value.GetToken(tokenRequestContext, CancellationToken.None);

            // Decode the JWT to get user identity information
            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadToken(token.Token) as JwtSecurityToken;
            ApplicationId = jsonToken?.Claims?.FirstOrDefault(claim => claim.Type == "appid")?.Value ?? "Claim appid (Application Id) Not Found";
            TenantId = jsonToken?.Claims?.FirstOrDefault(claim => claim.Type == "tid")?.Value ?? "Claim tid (Tenant Id) Not Found";
        }
        catch
        {
            ApplicationId = "Failed To Read Claims Data: appid";
            TenantId = "Failed To Read Claims Data: tid";
        }
    }
}
