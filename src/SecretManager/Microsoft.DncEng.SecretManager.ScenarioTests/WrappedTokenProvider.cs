using System.Threading.Tasks;
using Azure.Core;

namespace Microsoft.DncEng.SecretManager.Tests
{
    public sealed class WrappedTokenProvider : ITokenCredentialProvider
    {
        private readonly TokenCredential _tokenCredential;

        public WrappedTokenProvider(TokenCredential tokenCredential)
        {
            _tokenCredential = tokenCredential;
        }

        public Task<TokenCredential> GetCredentialAsync() => Task.FromResult(_tokenCredential);
    }
}
