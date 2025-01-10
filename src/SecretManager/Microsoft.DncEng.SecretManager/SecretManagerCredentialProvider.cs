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
    }

    // Expect AzureCliCredential for CI and local dev environments. 
    // Use InteractiveBrowserCredential as a fall back for local dev environments.
    private readonly Lazy<TokenCredential> _credential = new(() =>
    {
        return new ChainedTokenCredential(
            new AzureCliCredential(new AzureCliCredentialOptions { TenantId = ConfigurationConstants.MsftAdTenantId }),
            new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions() { TenantId = ConfigurationConstants.MsftAdTenantId })
        );
        });
    public Task<TokenCredential> GetCredentialAsync()
    {
        var result = Task.FromResult(_credential.Value);
        // We call SetCredentialIdentityValues here because _credential is set
        // by a lazy action that does not run until it is first needed
        // If this were called in the constructor it would force _credential to
        // populate as soon as the object was instantiated which would change
        // the current process behavior
        SetCredentialIdentityValues();
        return result;
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
            ApplicationId = jsonToken?.Claims?.FirstOrDefault(claim => claim.Type == "appid")?.Value ?? "";
            TenantId = jsonToken?.Claims?.FirstOrDefault(claim => claim.Type == "tid")?.Value ?? "";
        }
        catch
        {
            // We swallow all errors here to ensure that no part of the audit logging process can cause the application to fail.
            // These values are not critical to the operation of the application and are only used for audit logging purposes.
        }
    }
}
