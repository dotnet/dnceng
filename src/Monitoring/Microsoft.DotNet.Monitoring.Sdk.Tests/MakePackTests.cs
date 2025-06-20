using System;
using System.Collections.Generic;
using System.IO;
using AwesomeAssertions;
using Microsoft.DotNet.Monitoring.Sdk;
using NUnit.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotNet.Grafana.Tests;

[TestFixture]
public class MakePackTests
{
    [Test]
    public void ExtractDataSourceIdentifiersTest()
    {
        //$.dashboard.[*].datasource
        var dashboard = new JObject
        {
            {
                "dashboard", 
                new JObject
                {
                    {  "annotations",
                        new JObject
                        {
                            {
                                "list", new JArray
                                {
                                    new JObject
                                    {
                                        {"datasource", "Test Datasource Name 1"},
                                    }
                                }
                            }
                        } 
                    }
                }
            },
            {
                "panels",
                new JArray
                {
                    new JObject
                    {
                        {
                            "datasource",
                            new JObject
                            {
                                {"type", "Test Datasource Type"},
                                {"uid", "1234"}
                            }
                        },
                        {"other-property", "IGNORED"},
                    }
                }
            },
            {"other-property", "IGNORED"}
        };

        var expected = new List<string> {
            "Test Datasource Name 1",
            "1234"
        };

        IEnumerable<string> actual = GrafanaSerialization.ExtractDataSourceIdentifiers(dashboard);

        actual.Should().Equal(expected);
    }

    [Test]
    public void SanitizeDataSourceTest()
    {
        var datasource = new JObject
        {
            {"id", "removed"},
            {"orgId", "removed"},
            {"url", ""},
            {"name", "datasource name"},
            {
                "jsonData",
                new JObject
                {
                    {"safeData1", "value 1"},
                    {"safeData2", "value 2"},
                }
            },
            {
                "secureJsonFields",
                new JObject
                {
                    {"dangerousField1", "REMOVED"},
                    {"dangerousField2", "REMOVED"},
                }
            },
        };

        var result = GrafanaSerialization.SanitizeDataSource(datasource);

        // These are instance dependent, so need to be stripped
        result["id"].Should().BeNull();
        result["orgId"].Should().BeNull();
        result["url"].Should().BeNull();
        result["secureJsonFields"].Should().BeNull();

        // These are secure, so they shouldn't be exported
        string df1 = result.Value<JObject>("secureJsonData")?.Value<string>("dangerousField1");
        df1.Should().StartWith("[vault(");
        df1.Should().Contain("dangerousField1");
        df1.Should().NotContain("REMOVED");

        string df2 = result.Value<JObject>("secureJsonData")?.Value<string>("dangerousField2");
        df2.Should().StartWith("[vault(");
        df2.Should().Contain("dangerousField2");
        df2.Should().NotContain("REMOVED");

        // This is safe, so it should be preserved
        (result.Value<JObject>("jsonData")?.Value<string>("safeData1")).Should().Be("value 1");
    }
}
