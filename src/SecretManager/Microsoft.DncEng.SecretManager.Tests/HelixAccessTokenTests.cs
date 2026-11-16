using System.Collections.Generic;
using AwesomeAssertions;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.SecretManager.SecretTypes;
using Moq;
using NUnit.Framework;

namespace Microsoft.DncEng.SecretManager.Tests;

[TestFixture]
public class HelixAccessTokenTests
{
    // Synthetic format-only fixture: 52 random-data characters, metadata, reserved data, signature, checksum.
    private static readonly string Secret = new string('a', 52) + "JQQJ99CL" + new string('A', 16) + "dnhp0123";

    [TestCase("0")]
    [TestCase("1")]
    [TestCase("2147483647")]
    [TestCase("0001")]
    public void ValidateToken_AcceptsVersionTwoTokens(string tokenId)
    {
        ValidateToken($"dnp2.{tokenId}.{Secret}").Should().BeTrue();
    }

    [TestCase("")]
    [TestCase("-1")]
    [TestCase("+1")]
    [TestCase("2147483648")]
    [TestCase("999999999999999999999999")]
    [TestCase("1.0")]
    [TestCase(" 1")]
    [TestCase("1 ")]
    [TestCase("\u0661")]
    public void ValidateToken_RejectsInvalidTokenIds(string tokenId)
    {
        ValidateToken($"dnp2.{tokenId}.{Secret}").Should().BeFalse();
    }

    [TestCaseSource(nameof(InvalidVersionTwoTokens))]
    public void ValidateToken_RejectsMalformedVersionTwoTokens(string token)
    {
        ValidateToken(token).Should().BeFalse();
    }

    [TestCase("abcdefghijklmnopqrstuvwx", true)]
    [TestCase("abcdefghijklmnopqrstuvwxyz0123456789_-", true)]
    [TestCase("abcdefghijklmnopqrstuvw", false)]
    [TestCase("abcdefghijklmnopqrstuvw+", false)]
    [TestCase("abcdefghijklmnopqrstuvw/", false)]
    [TestCase("abcdefghijklmnopqrstuvw=", false)]
    [TestCase("abcdefghijklmnopqrstuvwx\n", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void ValidateToken_ValidatesLegacyTokens(string token, bool expected)
    {
        ValidateToken(token).Should().Be(expected);
    }

    private static IEnumerable<string> InvalidVersionTwoTokens()
    {
        string token = $"dnp2.1.{Secret}";
        yield return token.Replace("dnp2.", "DNP2.");
        yield return token.Replace("dnp2.", "dnp3.");
        yield return $"dnp2.1{Secret}";
        yield return "dnp2.1.";
        yield return token[..^1];
        yield return token + "a";
        yield return token + ".extra";
        yield return token + "\n";
        yield return " " + token;
        yield return token + " ";
        yield return token.Replace("JQQJ", "JQQK");
        yield return token.Replace("99CL", "99CM");
        yield return token.Replace("dnhp", "DNHP");
        yield return token.Replace("dnhp", "abcd");
        foreach (char invalidCharacter in new[] { '+', '/', '_', '-', '=', '.', '\n' })
        {
            yield return $"dnp2.1.{invalidCharacter}{Secret[1..]}";
            yield return token[..^1] + invalidCharacter;
        }
    }

    private static bool ValidateToken(string token)
    {
        return new TestableHelixAccessToken().TestValidateToken(token);
    }

    private class TestableHelixAccessToken : HelixAccessToken
    {
        public TestableHelixAccessToken()
            : base(new Mock<ISystemClock>().Object, new Mock<IConsole>().Object)
        {
        }

        public bool TestValidateToken(string token) => ValidateToken(token);
    }
}
