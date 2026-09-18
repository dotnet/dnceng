using System.IO;
using System.Linq;
using System.Xml.Linq;
using AwesomeAssertions;
using NUnit.Framework;

namespace DotNet.Grafana.Tests;

[TestFixture]
public class SdkPropsTests
{
    [Test]
    public void TaskAssemblyPathDoesNotDependOnConsumerBuildProperties()
    {
        XDocument sdkProps = XDocument.Load(
            Path.Combine(TestContext.CurrentContext.TestDirectory, "Sdk.props"));

        string[] taskAssemblyPaths = sdkProps
            .Descendants("MicrosoftDotNetMonitoringSdkTasksAssembly")
            .Select(element => element.Value)
            .ToArray();

        taskAssemblyPaths.Should().NotBeEmpty();
        taskAssemblyPaths.Should().AllSatisfy(
            path => path.Should().NotContain("$(NetCurrent)"));
        taskAssemblyPaths.Should().AllSatisfy(
            path => path.Should().Contain("net10.0"));
    }
}
