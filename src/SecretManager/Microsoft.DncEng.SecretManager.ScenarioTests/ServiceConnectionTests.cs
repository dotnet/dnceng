using Azure.Core;
using FluentAssertions;
using Microsoft.DncEng.SecretManager.ServiceConnections;
using Microsoft.Extensions.Options;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using NUnit.Framework;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager.Tests
{
    [TestFixture]
    [Category("PostDeployment")]
    [Explicit("requires restricted resources, see inline comments")]
    public class ServiceConnectionTests : ScenarioTestsBase
    {
        /// <summary>
        /// This performs an end-to-end of service connection support by generating a new token, applying it to the service connection, and queuing a build that uses the service connection. The result of the build determines the result of this test.
        /// 
        /// Note: This test is marked "Explicit" so it does not run in regular CI. It requires access to dn-bot's credentials, which, to reduce security footprint, is not available to the scenario-test identity. This may be run locally by any developer with access to the necessary vaults.
        /// </summary>
        /// 
        /// <remarks>
        /// 
        /// This test uses a number of resources that must be properly configured for the test to pass.
        /// 
        /// <list type="bullet">
        /// <item>A non-public, non-anonymous Azure Artifacts feed with at least one package and located in a different Azure DevOps instance than the one hosting the tests. Currently, this is <see href="https://dev.azure.com/dnceng/internal/_artifacts/feed/secret-manager-scenario-test">secret-manager-scenario-test</see>.</item>
        /// <item>A NuGet service connection in the Azure DevOps instance hosting the tests to the feed configured above. A different organization is necessary because authentication happens under a different identity when used within the same instance. Currently, this is <see href="https://dev.azure.com/dnceng-stage/internal/_settings/adminservices?resourceId=abc764e7-fe2d-4c10-846b-4c819a2fd0de">secret-manager-scenario-test-connection-1</see>.</item>
        /// <item>A repository with an Azure DevOps pipeline definition file that performs some action using the service connection. The build should fail if the connection is not properly configured. Currently, this test uses the NuGet client to download a known package from the feed. Currently, this is <see href="https://dev.azure.com/dnceng-stage/internal/_git/secret-manager-scenario-test">secret-manager-scenario-test</see>.</item>
        /// <item>A pipeline created to execute the definition. This test will ask Azure DevOps to execute this pipeline and will use the result to determine success or failure of the test. Currently, this is <see href="https://dev.azure.com/dnceng-stage/internal/_build?definitionId=13">secret-manager-scenario-test</see>.</item>
        /// </list>
        /// 
        /// The test involves two identities. 
        /// 
        /// The first is the identity used by the scenario test runner. This identity is used both for the setup and teardown of tests as well as execution of secret-manager itself. This identity requires permissions:
        /// <list type="bullet">
        /// <item><c>vso.serviceendpoint_manage</c> to read and write to the Service Endpoints. This is used first in test setup to prepare the service connection, then by secret-manager to rotate the secret.</item>
        /// <item><c>vso.build_execute</c> to allow the test to queue a build of the test pipeline.</item>
        /// </list>
        /// The second identity is used by secret-manager to generate the token to be used by the service connection. This identity needs <c>vso.packaging</c> permission to read from the Azure Artifacts feed used in the test.
        /// <br />
        /// Steps performed are:
        /// 
        /// <list type="number"><item>Clear the description field of the service connection. This ensures that secret-manager will rotate the secret instead of detecting that the secret is still valid and not in need of rotation. Since this would invalidate the result even if the test ultimately succeeds, the test checks that the description is in fact empty before proceeding and will fail the test at this point if it is not.</item>
        /// <item>Execute a typical "secret-manager synchronize" command using the manifest defined in-code.</item>
        /// <item>Queue a build in the Azure DevOps instance that exercises the service connection.</item>
        /// <item>Wait for the build to complete.</item>
        /// <item>Evaluate the result of the build. If the build "succeeded" (in Azure DevOps terms), the test is marked as passing. Any other build result causes failure.</item></list>
        /// 
        /// This is the Pipeline Definition used:
        /// <code>
        /// trigger: none
        /// pr: none
        /// steps:
        /// - task: NuGetAuthenticate@1
        ///   inputs:
        ///     nuGetServiceConnections: secret-manager-scenario-test-connection-1
        /// - pwsh: |
        ///     nuget install Azure.Core -Version 1.42.0 -DependencyVersion Ignore -DirectDownload -Source https://pkgs.dev.azure.com/dnceng/internal/_packaging/secret-manager-scenario-test/nuget/v3/index.json
        /// </code>
        /// 
        /// </remarks>
        [Test, CancelAfter(300_000)]
        public async Task TestWithBuild(CancellationToken cancellationToken = default)
        {
            // The definition ID of the pipeline that will exercise the service connection
            int scenarioTestDefinitionId = 13;

            // The Azure DevOps project in which the test pipeline lives
            string scenarioTestAzdoProject = "internal";

            // The Azure DevOps organization where the test pipeline and service connection live
            string scenarioTestAzdoOrganization = "dnceng-stage";

            // The full URI for the Azure DevOps instance
            Uri scenarioTestAzdoBaseUri = new ($"https://dev.azure.com/{scenarioTestAzdoOrganization}");

            // The ID of the service endpoint used in this test
            Guid scenarioTestServiceEndpointId = new("abc764e7-fe2d-4c10-846b-4c819a2fd0de");

            // A full, proper manifest identifying the service connection to rotate
            string manifest = """
                storageLocation:
                  type: azure-devops-project
                  parameters:
                    organization: dnceng-stage
                    project: internal
                references:
                  helixkv:
                    type: azure-key-vault
                    parameters:
                      subscription: a4fc5514-21a9-4296-bfaf-5c7ee7fa35d1
                      name: helixkv
                secrets:
                  secret-manager-scenario-test-connection-1:
                    type: azure-devops-service-endpoint
                    parameters:
                      authorization:
                        type: azure-devops-access-token
                        parameters:
                          domainAccountName: dn-bot
                          domainAccountSecret:
                            location: helixkv
                            name: dn-bot-account-redmond
                          organizations: dnceng
                          scopes: packaging
                """;

            // Delay between attempts when polling for build status
            TimeSpan pollingDelay = TimeSpan.FromSeconds(1);

            // Setup: Clear service connection description as a means to ensure rotation will happen
            TestContext.Progress.WriteLine("Clearing service connection description...");
            using (HttpClient httpClient = new())
            {
                IOptions<ServiceEndpointClient.Configuration> serviceEndpointClientOptions = Options.Create<ServiceEndpointClient.Configuration>(new(scenarioTestAzdoOrganization, scenarioTestAzdoProject));

                ServiceEndpointClient serviceEndpointClient = new(httpClient, serviceEndpointClientOptions, new WrappedTokenProvider(_tokenCredential));

                ServiceEndpointUpdateData updateData = new() { Description = string.Empty, AccessToken = "placeholder" };
                await serviceEndpointClient.Update(scenarioTestServiceEndpointId, updateData, cancellationToken);

                // Verify that the endpoint was cleared
                ServiceEndpoint serviceEndpoint = await serviceEndpointClient.Get(scenarioTestServiceEndpointId, cancellationToken);
                serviceEndpoint.Description.Should().BeEmpty("because this ensures the secret will be rotated");
            }

            // Setup: Rotate the service connection secret
            TestContext.Progress.WriteLine("Rotating service connection secret...");
            await ExecuteSynchronizeCommand(manifest);

            // Action: Queue a build that will exercise the service connection
            TokenRequestContext requestContext = new(["499b84ac-1321-427f-aa17-267ca6975798"]);
            AccessToken token = await _tokenCredential.GetTokenAsync(requestContext, cancellationToken);

            VssCredentials credentials = new(new VssBasicCredential(string.Empty, token.Token));

            using VssConnection tokenConnection = new (scenarioTestAzdoBaseUri, credentials);
            using BuildHttpClient buildClient = await tokenConnection.GetClientAsync<BuildHttpClient>(cancellationToken);

            Build testBuild = new()
            { 
               Definition = new() { Id = scenarioTestDefinitionId },
            };
            
            TestContext.Progress.Write($"Queuing build of definition {testBuild.Definition.Id} in project {scenarioTestAzdoProject} of Azure DevOps instance {scenarioTestAzdoBaseUri}...");
            Build actualTestBuild = await buildClient.QueueBuildAsync(testBuild, scenarioTestAzdoProject, cancellationToken: cancellationToken);
            TestContext.Progress.WriteLine($"Successfully queued build {actualTestBuild.Id}");

            TestContext.Progress.Write($"Polling for build completion with interval {pollingDelay}...");
            while (actualTestBuild.Status != BuildStatus.Completed)
            {
                TestContext.Progress.Write(".");
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(pollingDelay, cancellationToken);

                actualTestBuild = await buildClient.GetBuildAsync(actualTestBuild.Project.Id, actualTestBuild.Id, cancellationToken: cancellationToken);
            }

            // Test: If the build succeeded, assume the service connection is authenticating properly
            actualTestBuild.Result.Should().Be(BuildResult.Succeeded);
        }
    }
}
