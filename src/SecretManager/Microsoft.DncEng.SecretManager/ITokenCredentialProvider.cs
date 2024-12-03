using System.Threading.Tasks;
using Azure.Core;

namespace Microsoft.DncEng.SecretManager;

public interface ITokenCredentialProvider
{
    public Task<TokenCredential> GetCredentialAsync();
}
