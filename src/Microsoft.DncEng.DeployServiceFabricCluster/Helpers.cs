using System.Net.Http.Headers;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Rest;
using Microsoft.Rest.TransientFaultHandling;

namespace Microsoft.DncEng.DeployServiceFabricCluster;

internal static class Helpers
{
    private const string MsftAdTenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47";
    private static readonly DefaultAzureCredential TokenProvider = new DefaultAzureCredential();

    private class AzureCredentialsTokenProvider : ITokenProvider
    {
        private readonly DefaultAzureCredential _inner;

        public AzureCredentialsTokenProvider(DefaultAzureCredential inner)
        {
            _inner = inner;
        }

        public async Task<AuthenticationHeaderValue> GetAuthenticationHeaderAsync(CancellationToken cancellationToken)
        {
            AccessToken accessToken = await _inner.GetTokenAsync(
                new TokenRequestContext(scopes: new string[] { "https://management.azure.com/.default" }, tenantId: MsftAdTenantId) { }
            );

            string token = accessToken.Token;
            return new AuthenticationHeaderValue("Bearer", token);
        }
    }

    public static (IAzure, IResourceManager) Authenticate(string subscriptionId)
    {
        string version = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "1.0.0";

        var tokenCredentials = new TokenCredentials(new AzureCredentialsTokenProvider(TokenProvider));
        var credentials = new AzureCredentials(tokenCredentials, null, MsftAdTenantId, AzureEnvironment.AzureGlobalCloud);

        HttpLoggingDelegatingHandler.Level logLevel = HttpLoggingDelegatingHandler.Level.Headers;
        var retryPolicy = new RetryPolicy(new DefaultTransientErrorDetectionStrategy(), 5);
        var programName = "DncEng Service Fabric Cluster Creator";

        return (Azure.Management.Fluent.Azure.Configure()
                .WithLogLevel(logLevel)
                .WithRetryPolicy(retryPolicy)
                .WithUserAgent(programName, version)
                .Authenticate(credentials)
                .WithSubscription(subscriptionId),
            ResourceManager.Configure()
                .WithLogLevel(logLevel)
                .WithRetryPolicy(retryPolicy)
                .WithUserAgent(programName, version)
                .Authenticate(credentials)
                .WithSubscription(subscriptionId));
    }
}
