using System.Threading.Tasks;
using Azure.Core;

namespace Microsoft.DncEng.SecretManager;

public interface ITokenCredentialProvider
{
    /// <summary>
    /// The applicatoin ID for the credential provider.
    /// </summary>
    public string ApplicationId { get; }

    /// <summary>
    /// The tenant ID that provided the token from the credential provider.
    /// </summary>
    public string TenantId { get; }

    public Task<TokenCredential> GetCredentialAsync();
}
