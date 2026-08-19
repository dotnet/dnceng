using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Microsoft.DotNet.Monitoring.Sdk.Tests;

internal class VaultSecretReferenceTests
{
    [TestCase("[vault(secret-name)]", "default-vault", "secret-name")]
    [TestCase("[Vault(secret-name)]", "default-vault", "secret-name")]
    [TestCase("[VAULT(external-vault/secret-name)]", "external-vault", "secret-name")]
    public void TryGetSecretReference_ParsesSupportedReferences(
        string reference,
        string expectedVault,
        string expectedSecret)
    {
        bool parsed = DeployPublisher.TryGetSecretReference(
            reference,
            "default-vault",
            out string vault,
            out string secret);

        parsed.Should().BeTrue();
        vault.Should().Be(expectedVault);
        secret.Should().Be(expectedSecret);
    }

    [TestCase("prefix [vault(secret-name)]")]
    [TestCase("[vault(secret-name)] suffix")]
    [TestCase("[vault()]")]
    [TestCase("[vault(vault-name/)]")]
    [TestCase("[vault(vault-name/secret-name/extra)]")]
    [TestCase("[vault(vault name/secret-name)]")]
    [TestCase("[vault(vault-name/secret name)]")]
    public void TryGetSecretReference_RejectsInvalidReferences(string reference)
    {
        bool parsed = DeployPublisher.TryGetSecretReference(
            reference,
            "default-vault",
            out string vault,
            out string secret);

        parsed.Should().BeFalse();
        vault.Should().BeNull();
        secret.Should().BeNull();
    }

    [Test]
    public async Task ReplaceVaultAsync_ReplacesNestedDefaultAndNamedVaultReferences()
    {
        JObject data = new()
        {
            ["default"] = "[vault(default-secret)]",
            ["nested"] = new JArray
            {
                new JObject
                {
                    ["external"] = "[vault(external-vault/external-secret)]",
                    ["unchanged"] = "prefix [vault(not-a-reference)]"
                }
            }
        };
        List<(string Vault, string Secret)> requests = new();

        JToken result = await DeployPublisher.ReplaceVaultAsync(
            data,
            "default-vault",
            (vault, secret) =>
            {
                requests.Add((vault, secret));
                return Task.FromResult($"{vault}:{secret}");
            });

        result["default"]?.Value<string>().Should().Be("default-vault:default-secret");
        result["nested"]?[0]?["external"]?.Value<string>().Should().Be("external-vault:external-secret");
        result["nested"]?[0]?["unchanged"]?.Value<string>().Should().Be("prefix [vault(not-a-reference)]");
        requests.Should().Equal(
            ("default-vault", "default-secret"),
            ("external-vault", "external-secret"));
    }
}
