using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("azure-storage-blob-sas-uri")]
public class AzureStorageBlobSasUri : SecretType<AzureStorageBlobSasUri.Parameters>
{
    public class Parameters
    {
        public SecretReference ConnectionString { get; set; }
        public string Container { get; set; }
        public string Blob { get; set; }
        public string Permissions { get; set; }
    }

    private readonly ISystemClock _clock;

    public AzureStorageBlobSasUri(ISystemClock clock)
    {
        _clock = clock;
    }

    protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.UtcNow;
        DateTimeOffset expiration = now.AddMonths(1);
        DateTimeOffset nextRotation = now.AddDays(15);

        string connectionString = await context.GetSecretValue(parameters.ConnectionString);
        BlobClient blob = new(connectionString, parameters.Container, parameters.Blob);

        BlobSasPermissions blobSasPermissions = StorageUtils.BlobSasPermissionsFromString(parameters.Permissions);

        string result = blob.GenerateSasUri(blobSasPermissions, expiration).AbsoluteUri;

        return new SecretData(result, expiration, nextRotation);
    }
}
