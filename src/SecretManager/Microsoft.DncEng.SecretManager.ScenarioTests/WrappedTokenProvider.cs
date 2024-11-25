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
                // Get a token from the credential provider
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
                // We swallow all errors here to ensure that no part of the audit logging process can cause the application to fail.
                // These values are not critical to the operation of the application and are only used for audit logging purposes.
            }
        }

        public Task<TokenCredential> GetCredentialAsync() => Task.FromResult(_tokenCredential);
    }
}
