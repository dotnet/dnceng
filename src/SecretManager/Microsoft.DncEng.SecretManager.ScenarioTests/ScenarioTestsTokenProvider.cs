using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;

namespace Microsoft.DncEng.SecretManager.Tests
{
    public sealed class ScenarioTestsTokenProvider : ITokenCredentialProvider
    {
        private readonly TokenCredential _tokenCredential = new EnvironmentCredential();

        public Task<TokenCredential> GetCredentialAsync() => Task.FromResult(_tokenCredential);
    }
}
