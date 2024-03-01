using System;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;

namespace Microsoft.DncEng.SecretManager;

public sealed class SecretManagerCredentialProvider : ITokenCredentialProvider
{
    // Expect AzureCliCredential for CI and local dev environments. 
    // Use InteractiveBrowserCredential as a fallback for local dev environments.
    private readonly Lazy<TokenCredential> _credential = new(() =>
        new ChainedTokenCredential(
            new AzureCliCredential(new AzureCliCredentialOptions { TenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47" }),
            new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions() { TenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47" })
        ));

    public Task<TokenCredential> GetCredentialAsync()
    {
        return Task.FromResult(_credential.Value);
    }
}
