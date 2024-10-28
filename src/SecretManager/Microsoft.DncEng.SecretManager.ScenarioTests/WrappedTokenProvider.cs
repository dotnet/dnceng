using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;

namespace Microsoft.DncEng.SecretManager.Tests
{
    public sealed class WrappedTokenProvider : ITokenCredentialProvider
    {
        private readonly TokenCredential _tokenCredential;

        /// <inheritdoc/>
        public string ApplicationId { get; internal set; }
        /// <inheritdoc/>
        public string TenantId { get; internal set; }

        public WrappedTokenProvider(TokenCredential tokenCredential)
        {
            _tokenCredential = tokenCredential;
            SetCredentialIdentityValues();
        }

        /// <inheritdoc/>
        internal void SetCredentialIdentityValues()
        {
            try
            {
                // Get a token from the crendential provider
                var tokenRequestContext = new TokenRequestContext(new[] { "https://management.azure.com/.default" });
                var token = _tokenCredential.GetToken(tokenRequestContext, CancellationToken.None);

                // Decode the JWT to get user identity information
                var handler = new JwtSecurityTokenHandler();
                var jsonToken = handler.ReadToken(token.Token) as JwtSecurityToken;
                ApplicationId = jsonToken?.Claims?.FirstOrDefault(claim => claim.Type == "oid")?.Value ?? "Claim Oid Not Found";
                TenantId = jsonToken?.Claims?.FirstOrDefault(claim => claim.Type == "tenant_id")?.Value ?? "Claim tenant_id Not Found";
            }
            catch
            {
                ApplicationId = "Failed To Read Claims Data: Oid";
                TenantId = "Failed To Read Claims Data: tenant_id";
            }
        }

        public Task<TokenCredential> GetCredentialAsync() => Task.FromResult(_tokenCredential);
    }
}
