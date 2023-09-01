using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.DncEng.CommandLineLib.Authentication;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.Rest;

namespace Microsoft.DncEng.SecretManager;

public static class TokenCredentialExtensions
{
    /// <summary>
    /// Returns credentials that will first try to use Azure CLI credentials, then fall back to the provided credentials.
    /// </summary>
    public static TokenCredential WithAzureCliCredentials(this TokenCredential otherCredentials)
    {
        return new ChainedTokenCredential(
            new AzureCliCredential(
                new AzureCliCredentialOptions()
                {
                    TenantId = ConfigurationConstants.MsftAdTenantId
                }), otherCredentials);
    }

    /// <summary>
    /// Retrieves token credentials by first trying to use Azure CLI credentials, then falling back to the default.
    /// </summary>
    public static async Task<TokenCredentials> GetTokenCredentialsUsingAzureCli(
        this TokenCredentialProvider tokenCredential,
        string resourceId = "https://management.azure.com/.default",
        CancellationToken cancellationToken = default)
    {
        // Get default way of getting credentials (e.g. from user's environment)
        TokenCredential credentials = await tokenCredential.GetCredentialAsync();

        // Add Azure CLI credentials as a first possibility
        credentials = new ChainedTokenCredential(
            new AzureCliCredential(
                new AzureCliCredentialOptions()
                {
                    TenantId = ConfigurationConstants.MsftAdTenantId
                }), credentials);


        var token = await credentials.GetTokenAsync(new TokenRequestContext(new[]
        {
            resourceId,
        }), cancellationToken);

        return new TokenCredentials(token.Token);
    }
}
