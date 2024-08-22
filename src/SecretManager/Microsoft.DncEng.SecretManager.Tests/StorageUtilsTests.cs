using FluentAssertions;
using Moq;
using NUnit.Framework;
using System;

namespace Microsoft.DncEng.SecretManager.Tests;

#nullable enable

public class StorageUtilsTests
{
    [TestCase("AccountName=PlaceholderAccountName", "PlaceholderAccountName")]
    [TestCase("AccountName=PlaceholderAccountName;AccountKey=Placeholder", "PlaceholderAccountName")]
    [TestCase("DefaultEndpointsProtocol=https;AccountName=PlaceholderAccountName;AccountKey=placeholder;BlobEndpoint=https://placeholder.blob.core.windows.net/;QueueEndpoint=https://placeholder.queue.core.windows.net/;TableEndpoint=https://placeholder.table.core.windows.net/;FileEndpoint=https://placeholder.file.core.windows.net/;", "PlaceholderAccountName")]
    public void CanParseAccountNameFromValidStrings(string testConnectionString, string expectedName)
    {
        bool actualResult = StorageUtils.TryParseStorageConnectionStringAccountName(testConnectionString, out string? actualName);

        actualResult.Should().BeTrue();
        expectedName.Should().Be(actualName);
    }

    [TestCase("AccountKey=PlaceholderAccountKey", "PlaceholderAccountKey")]
    [TestCase("AccountName=PlaceholderAccountName;AccountKey=PlaceholderAccountKey", "PlaceholderAccountKey")]
    [TestCase("DefaultEndpointsProtocol=https;AccountName=PlaceholderAccountName;AccountKey=PlaceholderAccountKey;BlobEndpoint=https://placeholder.blob.core.windows.net/;QueueEndpoint=https://placeholder.queue.core.windows.net/;TableEndpoint=https://placeholder.table.core.windows.net/;FileEndpoint=https://placeholder.file.core.windows.net/;", "PlaceholderAccountKey")]
    [TestCase("DefaultEndpointsProtocol=https;AccountName=PlaceholderAccountName;AccountKey=PlaceholderAccountKey", "PlaceholderAccountKey")]
    public void CanParseAccountKeyFromValidStrings(string testConnectionString, string expectedKey)
    {
        bool actualResult = StorageUtils.TryParseStorageConnectionStringAccountKey(testConnectionString, out string? actualKey);

        actualResult.Should().BeTrue();
        expectedKey.Should().Be(actualKey);
    }

    [TestCase(" ")]
    [TestCase("asdf")]
    [TestCase("AccountName=")]
    [TestCase("AccountName=;AccountKey=")]
    public void DoNotThrowFromInvalidString(string testConnectionString)
    {
        bool? actualResult = null;
        
        Action act = () => actualResult = StorageUtils.TryParseStorageConnectionStringAccountName(testConnectionString, out string? _);

        act.Should().NotThrow();
        actualResult.Should().BeFalse();
    }

    [Test]
    public void GenerateBlobContainerSasPrependsQueryStringSeparator()
    {
        string testConnectionString = "DefaultEndpointsProtocol=https;AccountName=PlaceholderAccountName;AccountKey=aaaaaaaaaaaaaaaabaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaabPLACEHOLDER";

        string testContainerName = "PlaceholderContainerName";
        string testPermissionsString = "l";
        DateTimeOffset testExpiresOn = DateTimeOffset.UtcNow.AddMonths(1);

        (string _, string actualSas) = StorageUtils.GenerateBlobContainerSas(testConnectionString, testContainerName, testPermissionsString, testExpiresOn);

        actualSas.Should().StartWith("?", "the query string separator is required for compatibility with standard set by Microsoft.WindowsAzure.Storage");
    }

    [Test]
    public void GenerateBlobAccountSasPrependsQueryStringSeparator()
    {
        string testConnectionString = "DefaultEndpointsProtocol=https;AccountName=PlaceholderAccountName;AccountKey=aaaaaaaaaaaaaaaabaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaabPLACEHOLDER";

        string testPermissionsString = "rl";
        string testServiceName = "blob";
        DateTimeOffset testExpiresOn = DateTimeOffset.UtcNow.AddMonths(1);

        (string _, string actualSas) = StorageUtils.GenerateBlobAccountSas(testConnectionString, testPermissionsString, testServiceName, testExpiresOn);

        actualSas.Should().StartWith("?", "the query string separator is required for compatibility with standard set by Microsoft.WindowsAzure.Storage");
    }
}
