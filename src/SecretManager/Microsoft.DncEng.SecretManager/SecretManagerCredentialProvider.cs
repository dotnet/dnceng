using System;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.DncEng.Configuration.Extensions;

namespace Microsoft.DncEng.SecretManager;

public sealed class SecretManagerCredentialProvider : ITokenCredentialProvider
{
    // Expect AzureCliCredential for CI and local dev environments. 
    // Use InteractiveBrowserCredential as a fallback for local dev environments.
    private readonly Lazy<TokenCredential> _credential = new(() =>
        new ChainedTokenCredential(
            new AzureCliCredential(new AzureCliCredentialOptions { TenantId = ConfigurationConstants.MsftAdTenantId }),
            new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions() { TenantId = ConfigurationConstants.MsftAdTenantId })
        ));

    public Task<TokenCredential> GetCredentialAsync()
    {
        return Task.FromResult(_credential.Value);
    }
}
