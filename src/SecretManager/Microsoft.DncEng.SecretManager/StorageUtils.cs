using Azure.Core;
using Azure.Data.Tables.Sas;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Storage;
using Azure.ResourceManager.Storage.Models;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.DncEng.CommandLineLib.Authentication;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("Microsoft.DncEng.SecretManager.Tests")]

namespace Microsoft.DncEng.SecretManager;

#nullable enable

public static class StorageUtils
{
    public static async Task<string> RotateStorageAccountKey(string subscriptionId, string accountName, RotationContext context, TokenCredentialProvider tokenCredentialProvider, CancellationToken cancellationToken)
    {
        ArmClient armClient = new ArmClient(new DefaultAzureCredential());

        ResourceIdentifier subscriptionResourceId = SubscriptionResource.CreateResourceIdentifier(subscriptionId);
        SubscriptionResource subscriptionResource = armClient.GetSubscriptionResource(subscriptionResourceId);

        StorageAccountResource? account = await subscriptionResource.GetStorageAccountsAsync(cancellationToken)
            .FirstOrDefaultAsync(resource => resource.Id.Name.Equals(accountName, StringComparison.OrdinalIgnoreCase));

        if (account is null)
        {
            throw new ArgumentException($"Storage account '{accountName}' in subscription '{subscriptionId}' not found.");
        }

        string currentKey = context.GetValue("currentKey", "key1");
        string keyToReturn;
        StorageAccountRegenerateKeyContent regenerateKeyContent;

        switch (currentKey)
        {
            case "key1":
                regenerateKeyContent = new("key2");
                keyToReturn = "key2";
                break;

            case "key2":
                regenerateKeyContent = new("key1");
                keyToReturn = "key1";
                break;

            default:
                throw new InvalidOperationException($"Unexpected 'currentKey' value '{currentKey}'.");
        }

        List<StorageAccountKey> keys = await account.RegenerateKeyAsync(regenerateKeyContent, cancellationToken)
            .ToListAsync(cancellationToken: cancellationToken);

        StorageAccountKey key = keys.FirstOrDefault(k => k.KeyName == keyToReturn) ?? throw new InvalidOperationException($"Key {keyToReturn} not found.");
        context.SetValue("currentKey", keyToReturn);

        return key.Value;
    }

    public static AccountSasPermissions AccountSasPermissionsFromString(string input)
    {
        AccountSasPermissions accessAccountPermissions = default;
        foreach (char ch in input)
        {
            accessAccountPermissions |= ch switch
            {
                'a' => AccountSasPermissions.Add,
                'c' => AccountSasPermissions.Create,
                'd' => AccountSasPermissions.Delete,
                'l' => AccountSasPermissions.List,
                'r' => AccountSasPermissions.Read,
                'w' => AccountSasPermissions.Write,
                'u' => AccountSasPermissions.Update,
                'p' => AccountSasPermissions.Process,
                _ => throw new ArgumentOutOfRangeException(nameof(input)),
            };
        }

        return accessAccountPermissions;
    }

    public static (string accountUri, string sas) GenerateBlobAccountSas(string connectionString, string permissions, string service, DateTimeOffset expiryTime)
    {
        if (!TryParseStorageConnectionStringAccountKey(connectionString, out string? accountKey))
        {

            throw new ArgumentException("Failed to parse storage account key from connection string", nameof(connectionString));
        }

        if (!TryParseStorageConnectionStringAccountName(connectionString, out string? accountName))
        {

            throw new ArgumentException("Failed to parse storage account name from connection string", nameof(connectionString));
        }

        AccountSasBuilder sasBuilder = new();

        HashSet<string> servicesUsed = new(StringComparer.OrdinalIgnoreCase);
        foreach (string serviceString in service.Split("|"))
        {
            if (!servicesUsed.Add(serviceString))
            {
                throw new ArgumentOutOfRangeException(nameof(service));
            }

            sasBuilder.Services |= serviceString.ToLowerInvariant() switch
            {
                "blob" => AccountSasServices.Blobs,
                "table" => AccountSasServices.Tables,
                "file" => AccountSasServices.Files,
                "queue" => AccountSasServices.Queues,
                _ => throw new ArgumentOutOfRangeException(nameof(service)),
            };
        }

        sasBuilder.ExpiresOn = expiryTime;
        sasBuilder.SetPermissions(AccountSasPermissionsFromString(permissions));
        sasBuilder.ResourceTypes = AccountSasResourceTypes.All;
        sasBuilder.Protocol = SasProtocol.Https;

        string sas = sasBuilder.ToSasQueryParameters(
            sharedKeyCredential: new StorageSharedKeyCredential(accountName, accountKey)
        ).ToString();

        string uri = $"https://{accountName}.blob.core.windows.net/";

        return (uri, sas);
    }

    public static (string containerUri, string sas) GenerateBlobContainerSas(string connectionString, string containerName, string permissions, DateTimeOffset expiryTime)
    {
        BlobContainerSasPermissions containerSasPermissions = BlobContainerSasPermissionsFromString(permissions);

        BlobContainerClient containerClient = new(connectionString, containerName);
        Uri sasUri = containerClient.GenerateSasUri(containerSasPermissions, expiryTime);

        BlobUriBuilder blobUriBuilder = new(sasUri);

        // Prepend the query string separator to match output from Microsoft.WindowsAzure.Storage
        string sas = '?' + blobUriBuilder.Sas.ToString();

        blobUriBuilder.Sas = null;
        string containerUri = blobUriBuilder.ToString();

        return (containerUri, sas);
    }

    internal static bool TryParseStorageConnectionStringAccountKey(string connectionString, [NotNullWhen(true)] out string? accountKey)
    {
        return TryParseStorageConnectionStringElement(connectionString, "AccountKey", out accountKey);
    }

    internal static bool TryParseStorageConnectionStringAccountName(string connectionString, [NotNullWhen(true)] out string? accountName)
    {
        return TryParseStorageConnectionStringElement(connectionString, "AccountName", out accountName);
    }

    internal static bool TryParseStorageConnectionStringElement(string connectionString, string elementName, [NotNullWhen(true)] out string? elementValue)
    {
        string elementNameWithDelimiter = elementName + "=";
        string[] splitConnectionString = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);

        if (splitConnectionString.Length == 0)
        {
            elementValue = null;
            return false;
        }

        string? element = splitConnectionString
            .FirstOrDefault(element => element.StartsWith(elementNameWithDelimiter, StringComparison.OrdinalIgnoreCase));

        if (element is null)
        {
            elementValue = null;
            return false;
        }

        elementValue = element[elementNameWithDelimiter.Length..];

        if (elementValue.Length == 0)
        {
            elementValue = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Constructs a BlobContainerSasPermissions object from a permissions string.
    /// </summary>
    /// <remarks>Equivalent to <seealso cref="Microsoft.Azure.Storage.Blob.SharedAccessBlobPolicy.PermissionsFromString(string)"></seealso> and intended to provide a bridge when migrating between SDK versions.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if a flag is unrecognized</exception>
    public static BlobContainerSasPermissions BlobContainerSasPermissionsFromString(string permissions)
    {
        BlobContainerSasPermissions containerSasPermissions = default;

        foreach (char c in permissions.ToLowerInvariant())
        {
            containerSasPermissions = c switch
            {
                'r' => containerSasPermissions | BlobContainerSasPermissions.Read,
                'w' => containerSasPermissions | BlobContainerSasPermissions.Write,
                'd' => containerSasPermissions | BlobContainerSasPermissions.Delete,
                'l' => containerSasPermissions | BlobContainerSasPermissions.List,
                'a' => containerSasPermissions | BlobContainerSasPermissions.Add,
                'c' => containerSasPermissions | BlobContainerSasPermissions.Create,
                _ => throw new ArgumentOutOfRangeException(nameof(permissions), $"Unrecognized container permissions flag '{c}'"),
            };
        }

        return containerSasPermissions;
    }

    /// <summary>
    /// Constructs a BlobContainerSasPermissions object from a permissions string.
    /// </summary>
    /// <remarks>Equivalent to <seealso cref="Microsoft.Azure.Storage.Blob.SharedAccessBlobPolicy.PermissionsFromString(string)"></seealso> and intended to provide a bridge when migrating between SDK versions.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if a flag is unrecognized</exception>
    public static BlobSasPermissions BlobSasPermissionsFromString(string permissions)
    {
        BlobSasPermissions blobSasPermissions = default;

        foreach (char c in permissions.ToLowerInvariant())
        {
            blobSasPermissions = c switch
            {
                'r' => blobSasPermissions | BlobSasPermissions.Read,
                'w' => blobSasPermissions | BlobSasPermissions.Write,
                'd' => blobSasPermissions | BlobSasPermissions.Delete,
                'l' => blobSasPermissions | BlobSasPermissions.List,
                'a' => blobSasPermissions | BlobSasPermissions.Add,
                'c' => blobSasPermissions | BlobSasPermissions.Create,
                _ => throw new ArgumentOutOfRangeException(nameof(permissions), $"Unrecognized blob permissions flag '{c}'"),
            };
        }

        return blobSasPermissions;
    }

    /// <summary>
    /// Constructs a TableSasPermissions object from a permissions string.
    /// </summary>
    /// <remarks>Equivalent to <seealso cref="Microsoft.Azure.Storage.Table.SharedAccessTablePolicy.PermissionsFromString(string)"></seealso> and intended to provide a bridge when migrating between SDK versions.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if a flag is unrecognized</exception>
    public static TableSasPermissions TableSasPermissionsFromString(string permissions)
    {
        TableSasPermissions tableSasPermissions = default;

        foreach (char c in permissions.ToLowerInvariant())
        {
            tableSasPermissions = c switch
            {
                'r' => tableSasPermissions | TableSasPermissions.Read,
                'a' => tableSasPermissions | TableSasPermissions.Add,
                'u' => tableSasPermissions | TableSasPermissions.Update,
                'd' => tableSasPermissions | TableSasPermissions.Delete,
                _ => throw new ArgumentOutOfRangeException(nameof(permissions), $"Unrecognized table permissions flag '{c}'"),
            };
        }

        return tableSasPermissions;
    }
}
