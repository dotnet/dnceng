using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.EventHubs;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.CommandLineLib.Authentication;
using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure.ResourceManager.EventHubs.Models;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("event-hub-connection-string")]
public class EventHubConnectionString : SecretType<EventHubConnectionString.Parameters>
{
    public class Parameters
    {
        public Guid Subscription { get; set; }
        public string ResourceGroup { get; set; }
        public string Namespace { get; set; }
        public string Name { get; set; }
        public string Permissions { get; set; }
    }

    private readonly TokenCredentialProvider _tokenCredentialProvider;
    private readonly ISystemClock _clock;

    public EventHubConnectionString(TokenCredentialProvider tokenCredentialProvider, ISystemClock clock)
    {
        _tokenCredentialProvider = tokenCredentialProvider;
        _clock = clock;
    }

    private async Task<ArmClient> CreateManagementClient(Parameters parameters, CancellationToken cancellationToken)
    {
        TokenCredential serviceClientCredentials = await _tokenCredentialProvider.GetCredentialAsync();

        return new ArmClient(serviceClientCredentials, parameters.Subscription.ToString());
    }

    protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        ArmClient client = await CreateManagementClient(parameters, cancellationToken);

        ResourceIdentifier id = EventHubResource.CreateResourceIdentifier(
            parameters.Subscription.ToString(),
            parameters.ResourceGroup,
            parameters.Namespace,
            parameters.Name);

        EventHubResource eventHubResource = await client.GetEventHubResource(id).GetAsync(cancellationToken);

        string accessPolicyName = context.SecretName + "-access-policy";
        EventHubsAuthorizationRuleData rule = new();

        foreach (char c in parameters.Permissions)
        {
            switch (c)
            {
                case 's':
                    rule.Rights.Add(EventHubsAccessRight.Send);
                    break;
                case 'l':
                    rule.Rights.Add(EventHubsAccessRight.Listen);
                    break;
                case 'm':
                    rule.Rights.Add(EventHubsAccessRight.Manage);
                    break;
                default:
                    throw new ArgumentException($"Invalid permission specification '{c}'");
            }
        }

        ArmOperation<EventHubAuthorizationRuleResource> ruleResourceOperation = await eventHubResource.GetEventHubAuthorizationRules().CreateOrUpdateAsync(WaitUntil.Completed, accessPolicyName, rule, cancellationToken);
        EventHubAuthorizationRuleResource ruleResource = ruleResourceOperation.Value;

        var currentKey = context.GetValue("currentKey", "primary");
        EventHubsAccessKeys keys;
        string result;
        switch (currentKey)
        {
            case "primary":
                keys = await ruleResource.RegenerateKeysAsync(new EventHubsRegenerateAccessKeyContent(EventHubsAccessKeyType.SecondaryKey), cancellationToken);
                result = keys.SecondaryConnectionString;
                context.SetValue("currentKey", "secondary");
                break;
            case "secondary":
                keys = await ruleResource.RegenerateKeysAsync(new EventHubsRegenerateAccessKeyContent(EventHubsAccessKeyType.PrimaryKey), cancellationToken);
                result = keys.PrimaryConnectionString;
                context.SetValue("currentKey", "primary");
                break;
            default:
                throw new InvalidOperationException($"Unexpected 'currentKey' value '{currentKey}'.");
        }

        return new SecretData(result, DateTimeOffset.MaxValue, _clock.UtcNow.AddMonths(6));
    }
}
