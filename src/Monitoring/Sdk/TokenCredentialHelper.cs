using System.Collections.Concurrent;
using Azure.Identity;

namespace Microsoft.DotNet.Monitoring.Sdk;

internal static class TokenCredentialHelper
{
    private static readonly ChainedTokenCredential _defaultCredential = new(
        new DefaultAzureCredential(
            new DefaultAzureCredentialOptions
            {
                ExcludeEnvironmentCredential = true
            }
        )
    );

    private static ConcurrentDictionary<string, ChainedTokenCredential> CredentialCache { get; } = new ConcurrentDictionary<string, ChainedTokenCredential>();

    public static ChainedTokenCredential GetChainedTokenCredential(string managedIdentityId)
    {
        if (managedIdentityId == null)
        {
            return _defaultCredential;
        }

        if (CredentialCache.TryGetValue(managedIdentityId, out var chainedTokenCredential))
        {
            return chainedTokenCredential;
        }

        var credential = new ChainedTokenCredential(new ManagedIdentityCredential(managedIdentityId), new AzureCliCredential(), _defaultCredential);

        CredentialCache.TryAdd(managedIdentityId, credential);

        return credential;
    }
}
