using System;
using AwesomeAssertions;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.SecretManager.SecretTypes;
using Moq;
using NUnit.Framework;

namespace Microsoft.DncEng.SecretManager.Tests;

[TestFixture]
public class GitHubAccessTokenTests
{
    private TestableGitHubAccessToken _token;

    [SetUp]
    public void SetUp()
    {
        _token = new TestableGitHubAccessToken(new Mock<ISystemClock>().Object, new Mock<IConsole>().Object);
    }

    [Test]
    [TestCase("7", true, 7, Description = "Minimum allowed duration")]
    [TestCase("90", true, 90, Description = "Typical duration")]
    [TestCase("366", true, 366, Description = "Maximum allowed duration")]
    [TestCase("6", false, 6, Description = "Just below minimum")]
    [TestCase("1", false, 1, Description = "Below minimum")]
    [TestCase("0", false, 0, Description = "Zero")]
    [TestCase("367", false, 367, Description = "Above maximum")]
    [TestCase("-5", false, -5, Description = "Negative")]
    [TestCase("abc", false, 0, Description = "Not a number")]
    [TestCase("", false, 0, Description = "Empty string")]
    [TestCase("30.5", false, 0, Description = "Not a whole number")]
    public void TryParseExpirationInDays_ShouldValidateBounds(string value, bool expectedResult, int expectedParsed)
    {
        var result = _token.TestTryParseExpirationInDays(value, out var parsed);

        result.Should().Be(expectedResult);
        if (expectedResult)
        {
            parsed.Should().Be(expectedParsed);
        }
    }

    [Test]
    [TestCase(90, 60, Description = "1/3 of 90 days remains -> rotate after 60 days")]
    [TestCase(30, 20, Description = "1/3 of 30 days remains -> rotate after 20 days")]
    [TestCase(9, 6, Description = "Small duration")]
    [TestCase(7, 4, Description = "Minimum allowed duration")]
    public void ComputeNextRotationOn_ShouldRotateWhenAThirdRemains(int durationDays, int expectedDeltaDays)
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var nextRotationOn = _token.TestComputeNextRotationOn(now, durationDays);

        nextRotationOn.Should().Be(now.AddDays(expectedDeltaDays));
    }

    [Test]
    public void ComputeNextRotationOn_ShouldFallBeforeExpiration()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        const int durationDays = 90;

        var expiresOn = now.AddDays(durationDays);
        var nextRotationOn = _token.TestComputeNextRotationOn(now, durationDays);

        nextRotationOn.Should().BeAfter(now);
        nextRotationOn.Should().BeBefore(expiresOn);
    }

    // Test helper class exposing the protected static members for testing.
    private class TestableGitHubAccessToken : GitHubAccessToken
    {
        public TestableGitHubAccessToken(ISystemClock clock, IConsole console)
            : base(clock, console)
        {
        }

        public bool TestTryParseExpirationInDays(string value, out int parsedValue)
        {
            return TryParseExpirationInDays(value, out parsedValue);
        }

        public DateTimeOffset TestComputeNextRotationOn(DateTimeOffset now, int expirationInDays)
        {
            return ComputeNextRotationOn(now, expirationInDays);
        }
    }
}
