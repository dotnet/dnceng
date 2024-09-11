using System;
using FluentAssertions;
using Microsoft.DncEng.SecretManager.StorageTypes;
using NUnit.Framework;

namespace Microsoft.DncEng.SecretManager.Tests;

public class ServiceConnectionDescriptionBuilderTests
{
    private static readonly string[] invalidTestMagicStrings = [
        "Do not edit authentication. This is managed by secret-manager. Expires on 2019-13-03. Next rotation on 2019-12-01.",
        "Do not edit authentication. This is managed by secret-manager. Expires on 2019-12-03. Next rotation on 2019-13-01.",
        "Do not edit authentication. This is managed by secret-manager. Expires on 2019-13-03.",
        "Do not edit authentication. This is managed by secret-manager. Next rotation on 2019-12-01.",
        "Expires on 2019-12-03. Next rotation on 2019-12-01.",
        "",
        "asdf"
    ];

    private static readonly string validMagicString = "Do not edit authentication. This is managed by secret-manager. Expires on 2019-12-03. Next rotation on 2019-12-01.";
    private static readonly DateOnly validExpirationDate = new(2019, 12, 3);
    private static readonly DateOnly validNextRotationDate = new(2019, 12, 1);

    [Test]
    public void CanFormatValidDates()
    {
        string actualMagicString = ServiceConnectionDescriptionBuilder.CreateMagicString(validExpirationDate, validNextRotationDate);

        actualMagicString.Should().Be(validMagicString);
    }

    [Test]
    public void CanParseValidDates()
    {
        (DateOnly ExpirationDate, DateOnly NextRotationDate)? actualResult = ServiceConnectionDescriptionBuilder.ParseMagicString(validMagicString);

        actualResult.Should().NotBeNull();
        actualResult.Value.ExpirationDate.Should().Be(validExpirationDate);
        actualResult.Value.NextRotationDate.Should().Be(validNextRotationDate);
    }

    [TestCaseSource(nameof(invalidTestMagicStrings))]
    public void DoesNotThrow(string testString)
    {
        Action act = () => ServiceConnectionDescriptionBuilder.ParseMagicString(testString);
        act.Should().NotThrow();
    }

    [TestCaseSource(nameof(invalidTestMagicStrings))]
    public void ReturnsNullWithInvalidStrings(string testString)
    {
        (DateOnly, DateOnly)? actual = ServiceConnectionDescriptionBuilder.ParseMagicString(testString);
        actual.Should().BeNull();
    }
}
