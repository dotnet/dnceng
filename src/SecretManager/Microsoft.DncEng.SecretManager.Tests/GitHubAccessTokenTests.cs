using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
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
    [TestCase("20", true, 20, Description = "Typical duration")]
    [TestCase("30", true, 30, Description = "Maximum allowed duration")]
    [TestCase("6", false, 6, Description = "Just below minimum")]
    [TestCase("1", false, 1, Description = "Below minimum")]
    [TestCase("0", false, 0, Description = "Zero")]
    [TestCase("31", false, 31, Description = "Just above maximum")]
    [TestCase("90", false, 90, Description = "Above maximum")]
    [TestCase("-5", false, -5, Description = "Negative")]
    [TestCase("abc", false, 0, Description = "Not a number")]
    [TestCase("", false, 0, Description = "Empty string")]
    [TestCase("30.5", false, 0, Description = "Not a whole number")]
    public void TryParseExpirationInDays_ShouldValidateBounds(string value, bool expectedResult, int expectedParsed)
    {
        bool result = _token.TestTryParseExpirationInDays(value, out int parsed);

        result.Should().Be(expectedResult);
        if (expectedResult)
        {
            parsed.Should().Be(expectedParsed);
        }
    }

    [Test]
    [TestCase(30, 20, Description = "1/3 of 30 days remains -> rotate after 20 days")]
    [TestCase(15, 10, Description = "1/3 of 15 days remains -> rotate after 10 days")]
    [TestCase(9, 6, Description = "Small duration")]
    [TestCase(7, 4, Description = "Minimum allowed duration")]
    public void ComputeNextRotationOn_ShouldRotateWhenAThirdRemains(int durationDays, int expectedDeltaDays)
    {
        DateTimeOffset now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        DateTimeOffset nextRotationOn = _token.TestComputeNextRotationOn(now, durationDays);

        nextRotationOn.Should().Be(now.AddDays(expectedDeltaDays));
    }

    [Test]
    public void ComputeNextRotationOn_ShouldFallBeforeExpiration()
    {
        DateTimeOffset now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        const int durationDays = 30;

        DateTimeOffset expiresOn = now.AddDays(durationDays);
        DateTimeOffset nextRotationOn = _token.TestComputeNextRotationOn(now, durationDays);

        nextRotationOn.Should().BeAfter(now);
        nextRotationOn.Should().BeBefore(expiresOn);
    }

    [Test]
    public async Task RotateValue_ShouldPromptForExpirationAfterLoginInformationAndBeforePat()
    {
        DateTimeOffset now = new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);
        var interactions = new List<string>();
        var console = new Mock<IConsole>();
        console.SetupGet(c => c.IsInteractive).Returns(true);
        console.Setup(c => c.ShouldWrite(It.IsAny<VerbosityLevel>())).Returns(true);
        console.Setup(c => c.Write(It.IsAny<VerbosityLevel>(), It.IsAny<string>(), It.IsAny<ConsoleColor?>()))
            .Callback((VerbosityLevel _, string message, ConsoleColor? _) =>
            {
                if (message.StartsWith("Please login to", StringComparison.Ordinal))
                {
                    interactions.Add("Login information");
                }
            });
        console.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback((string _, string _) => interactions.Add("One-time password"))
            .ReturnsAsync(false);
        console.Setup(c => c.PromptAsync(It.IsAny<string>()))
            .Returns((string message) =>
            {
                interactions.Add(message);
                return Task.FromResult(message == "Enter expiration in days: "
                    ? "14"
                    : $"ghp_{new string('a', 36)}");
            });

        var clock = new Mock<ISystemClock>();
        clock.SetupGet(c => c.UtcNow).Returns(now);

        var storage = new Mock<StorageLocationType>();
        storage.Setup(s => s.GetSecretValueAsync(It.IsAny<IDictionary<string, object>>(), "bot-password"))
            .ReturnsAsync(CreateSecretValue("password"));
        storage.Setup(s => s.GetSecretValueAsync(It.IsAny<IDictionary<string, object>>(), "bot-otp"))
            .ReturnsAsync(CreateSecretValue("JBSWY3DPEHPK3PXP"));

        var context = new RotationContext(
            "token",
            ImmutableDictionary<string, string>.Empty,
            storage.Object.BindParameters(new Dictionary<string, object>()),
            ImmutableDictionary<string, StorageLocationType.Bound>.Empty);
        var parameters = new GitHubAccessToken.Parameters
        {
            GitHubBotAccountName = "bot-account",
            GitHubBotAccountSecret = new SecretReference("bot"),
        };
        var token = new GitHubAccessToken(clock.Object, console.Object);

        await token.RotateValues(parameters, context, CancellationToken.None);

        interactions.Should().ContainInOrder(
            "Login information",
            "One-time password",
            "Enter expiration in days: ",
            "Enter PAT: ");
    }

    private static SecretValue CreateSecretValue(string value)
    {
        return new SecretValue(
            value,
            ImmutableDictionary<string, string>.Empty,
            DateTimeOffset.MaxValue,
            DateTimeOffset.MaxValue);
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
