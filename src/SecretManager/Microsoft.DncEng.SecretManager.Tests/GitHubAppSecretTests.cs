using Microsoft.DncEng.SecretManager.SecretTypes;
using NUnit.Framework;

namespace Microsoft.DncEng.SecretManager.Tests;

[TestFixture]
public class GitHubAppSecretTests
{
    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("                ", false)]
    [TestCase("short", false)]
    [TestCase("123456789012345", false)]
    [TestCase("1234567890123456", true)]
    public void IsValidWebhookSecret_ValidatesMinimumLength(string value, bool expected)
    {
        Assert.That(GitHubAppSecret.IsValidWebhookSecret(value), Is.EqualTo(expected));
    }

    [Test]
    public void IsValidWebhookSecret_AcceptsMaximumLength()
    {
        Assert.That(GitHubAppSecret.IsValidWebhookSecret(new string('a', 128)), Is.True);
    }

    [Test]
    public void IsValidWebhookSecret_RejectsValuesOverMaximumLength()
    {
        Assert.That(GitHubAppSecret.IsValidWebhookSecret(new string('a', 129)), Is.False);
    }
}
