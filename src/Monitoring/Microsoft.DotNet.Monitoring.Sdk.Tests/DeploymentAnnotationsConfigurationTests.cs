using System;
using System.IO;
using AwesomeAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Microsoft.DotNet.Monitoring.Sdk.Tests;

internal class DeploymentAnnotationsConfigurationTests
{
    [TestCase("Production", "https://dotneteng-status.azurewebsites.net")]
    [TestCase("Staging", "https://dotneteng-status-staging.azurewebsites.net")]
    public void DatasourceUsesProvisionedAuthorizationHeader(string environment, string expectedUrl)
    {
        string repositoryRoot = FindRepositoryRoot();
        string datasourcePath = Path.Combine(
            repositoryRoot,
            "src",
            "Monitoring",
            "Monitoring.DncEng",
            "datasource",
            environment,
            "Deployment Annotations (Infinity).datasource.json");
        JObject datasource = JObject.Parse(File.ReadAllText(datasourcePath));

        datasource.Value<string>("url").Should().Be(expectedUrl);
        datasource.Value<bool?>("basicAuth").Should().NotBeTrue();
        datasource.SelectToken("$.jsonData.httpHeaderName1")?.Value<string>()
            .Should().Be("Authorization");
        datasource.SelectToken("$.secureJsonData.httpHeaderValue1")?.Value<string>()
            .Should().Be("[vault(dotneteng-status-auth-header)]");
        datasource.SelectToken("$.secureJsonData.basicAuthPassword").Should().BeNull();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "dnceng.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
