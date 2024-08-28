using Azure.Core;
using FluentAssertions;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager.Tests
{
    /* These tests use the Azure DevOps instance 'dnceng-stage'.
     * The service principal used by the secenario tests needs to be in the "Endpoint Administrator" group in the 'internal' project. 
     * 
     */

    [TestFixture]
    [Category("PostDeployment")]
    public class ServiceConnectionTests : ScenarioTestsBase
    {
        private const int ScenarioTestDefinitionId = 13;

        [Test, CancelAfter(300_000)]
        public async Task TestWithBuild(CancellationToken cancellationToken = default)
        {
            TokenRequestContext requestContext = new(["499b84ac-1321-427f-aa17-267ca6975798"]);
            AccessToken token = await _tokenCredential.GetTokenAsync(requestContext, cancellationToken);

            VssCredentials credentials = new(new VssBasicCredential(string.Empty, token.Token));

            using VssConnection tokenConnection = new (new Uri("https://vssps.dev.azure.com/"), credentials);
            using BuildHttpClient buildClient = await tokenConnection.GetClientAsync<BuildHttpClient>(cancellationToken);

            Build testBuild = new()
            { 
               Definition = new() { Id = ScenarioTestDefinitionId },
            };
            
            Build actualTestBuild = await buildClient.QueueBuildAsync(testBuild, cancellationToken: cancellationToken);

            while (actualTestBuild.Status != BuildStatus.Completed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

                actualTestBuild = await buildClient.GetBuildAsync(actualTestBuild.Project.Id, actualTestBuild.Id, cancellationToken: cancellationToken);
            }

            actualTestBuild.Result.Should().Be(BuildResult.Succeeded);
        }
    }
}
