using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using NUnit.Framework;

namespace Microsoft.DncEng.SecretManager.Tests
{
    public class SecretManifestTests
    {
        [Test]
        public void CanDeserialize()
        {
            var subscription = "007ae47e-f491-4706-a4ad-288c235dd30e";
            var vaultName = "pizza";
            var testManifest = $@"
subscription: {subscription}
name: {vaultName}
keys:
  key1:
    type: one
    size: 1
  key2:
    type: two
    size: 2
secrets:
  secret1:
    type: three
    parameters:
      one: 1
      two: ni
      three: san
  secret2:
    type: four
    parameters:
      a: yon
      b: cinco
      c: six
";

            var parsed = SecretManifest.Parse(new StringReader(testManifest));

            parsed.Should().BeEquivalentTo(new
            {
                Subscription = new Guid(subscription),
                Name = vaultName,
                Keys = new Dictionary<string, object>
                {
                    ["key1"] = new
                    {
                        Type = "one",
                        Size = 1,
                    },
                    ["key2"] = new
                    {
                        Type = "two",
                        Size = 2,
                    },
                },
                Secrets = new Dictionary<string, object>
                {
                    ["secret1"] = new
                    {
                        Type = "three",
                        Parameters = new Dictionary<string, string>
                        {
                            ["one"] = "1",
                            ["two"] = "ni",
                            ["three"] = "san"
                        },
                    },
                    ["secret2"] = new
                    {
                        Type = "four",
                        Parameters = new Dictionary<string, string>
                        {
                            ["a"] = "yon",
                            ["b"] = "cinco",
                            ["c"] = "six"
                        },
                    },
                },
            });
        }

    }
}
