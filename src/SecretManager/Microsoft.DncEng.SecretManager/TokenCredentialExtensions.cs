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

    public static async Task<TokenCredentials> GetTokenCredentials(
        this TokenCredentialProvider tokenCredential,
        string resourceId = "https://management.azure.com/.default",
        CancellationToken cancellationToken = default)
    {
        // Get default way of getting credentials (e.g. from user's environment)
        TokenCredential credentials = await tokenCredential.GetCredentialAsync();

        var token = await credentials.GetTokenAsync(new TokenRequestContext(new[]
        {
            resourceId,
        }), cancellationToken);

        return new TokenCredentials(token.Token);
    }
}
