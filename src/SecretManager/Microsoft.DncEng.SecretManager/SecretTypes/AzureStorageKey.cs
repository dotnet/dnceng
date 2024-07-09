using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("azure-storage-key")]
public class AzureStorageKey : SecretType<AzureStorageKey.Parameters>
{
    public class Parameters
    {
        public Guid Subscription { get; set; }
        public string Account { get; set; }
    }

    private readonly ITokenCredentialProvider _tokenCredentialProvider;
    private readonly ISystemClock _clock;

    public AzureStorageKey(ITokenCredentialProvider tokenCredentialProvider, ISystemClock clock)
    {
        _tokenCredentialProvider = tokenCredentialProvider;
        _clock = clock;
    }

    protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        string key = await StorageUtils.RotateStorageAccountKey(parameters.Subscription.ToString(), parameters.Account, context, _tokenCredentialProvider, cancellationToken);
        return new SecretData(key, DateTimeOffset.MaxValue, _clock.UtcNow.AddMonths(6));
    }
}
