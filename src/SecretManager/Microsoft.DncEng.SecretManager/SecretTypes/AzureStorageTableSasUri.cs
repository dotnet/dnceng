using Azure.Data.Tables;
using Azure.Data.Tables.Sas;
using Microsoft.DncEng.CommandLineLib;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("azure-storage-table-sas-uri")]
public class AzureStorageTableSasUri : SecretType<AzureStorageTableSasUri.Parameters>
{
    public class Parameters
    {
        public SecretReference ConnectionString { get; set; }
        public string Table { get; set; }
        public string Permissions { get; set; }
    }

    private readonly ISystemClock _clock;

    public AzureStorageTableSasUri(ISystemClock clock)
    {
        _clock = clock;
    }

    protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.UtcNow;
        DateTimeOffset expiresOn = now.AddMonths(1);
        DateTimeOffset nextRotationOn = now.AddDays(15);

        string connectionString = await context.GetSecretValue(parameters.ConnectionString);
        TableClient table = new (connectionString, parameters.Table);

        TableSasPermissions tableSasPermissions = StorageUtils.TableSasPermissionsFromString(parameters.Permissions);

        string result = table.GenerateSasUri(tableSasPermissions, expiresOn).AbsoluteUri;

        return new SecretData(result, expiresOn, nextRotationOn);
    }
}
