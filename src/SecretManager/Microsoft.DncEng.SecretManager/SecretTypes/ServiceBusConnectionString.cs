using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.ServiceBus;
using Azure.ResourceManager.ServiceBus.Models;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.CommandLineLib.Authentication;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("service-bus-connection-string")]
public class ServiceBusConnectionString : SecretType<ServiceBusConnectionString.Parameters>
{
    public class Parameters
    {
        public Guid Subscription { get; set; }
        public string ResourceGroup { get; set; }
        public string Namespace { get; set; }
        public string Permissions { get; set; }
    }

    private readonly TokenCredentialProvider _tokenCredentialProvider;
    private readonly ISystemClock _clock;

    public ServiceBusConnectionString(TokenCredentialProvider tokenCredentialProvider, ISystemClock clock)
    {
        _tokenCredentialProvider = tokenCredentialProvider;
        _clock = clock;
    }

    protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        TokenCredential credential = await _tokenCredentialProvider.GetCredentialAsync();
        ArmClient client = new(credential, parameters.Subscription.ToString());

        string accessPolicyName = context.SecretName + "-access-policy";

        var rule = new ServiceBusAuthorizationRuleData();

        bool updateRule = false;
        foreach (char c in parameters.Permissions)
        {
            switch (c)
            {
                case 's':
                    rule.Rights.Add(ServiceBusAccessRight.Send);
                    break;
                case 'l':
                    rule.Rights.Add(ServiceBusAccessRight.Listen);
                    break;
                case 'm':
                    rule.Rights.Add(ServiceBusAccessRight.Manage);
                    break;
                default:
                    throw new ArgumentException($"Invalid permission specification '{c}'");
            }
        }

        ResourceIdentifier ruleId = ServiceBusNamespaceAuthorizationRuleResource.CreateResourceIdentifier(parameters.Subscription.ToString(), parameters.ResourceGroup, parameters.Namespace, accessPolicyName);
        ServiceBusNamespaceAuthorizationRuleResource existingRule = client.GetServiceBusNamespaceAuthorizationRuleResource(ruleId);

        try
        {
            existingRule = await existingRule.GetAsync(cancellationToken);

            if (existingRule.Data.Rights.Count != rule.Rights.Count ||
                existingRule.Data.Rights.Zip(rule.Rights).Any((p) => p.First != p.Second))
            {
                updateRule = true;
            }
        }
        catch (RequestFailedException e) when (e.Status == (int)HttpStatusCode.NotFound)
        {
            updateRule = true;
        }

        if (updateRule)
        {
            ResourceIdentifier serviceBusNamespaceId = ServiceBusNamespaceResource.CreateResourceIdentifier(parameters.Subscription.ToString(), parameters.ResourceGroup, parameters.Namespace);
            ServiceBusNamespaceAuthorizationRuleCollection ruleCollection = client.GetServiceBusNamespaceResource(serviceBusNamespaceId).GetServiceBusNamespaceAuthorizationRules();

            existingRule = (await ruleCollection.CreateOrUpdateAsync(WaitUntil.Completed, accessPolicyName, rule, cancellationToken)).Value;
        }

        ResourceIdentifier serviceBusNamespaceResourceId = ServiceBusNamespaceResource.CreateResourceIdentifier(parameters.Subscription.ToString(), parameters.ResourceGroup, parameters.Namespace);
        ServiceBusNamespaceResource serviceBusNamespace = client.GetServiceBusNamespaceResource(serviceBusNamespaceResourceId);

        string currentKey = context.GetValue("currentKey", "primary");
        ServiceBusAccessKeys keys;

        string result;
        switch (currentKey)
        {
            case "primary":
                ServiceBusRegenerateAccessKeyContent regenerateSecondaryAccessKeyContent = new (ServiceBusAccessKeyType.SecondaryKey);
                keys = await existingRule.RegenerateKeysAsync(regenerateSecondaryAccessKeyContent, cancellationToken);
                result = keys.SecondaryConnectionString;
                context.SetValue("currentKey", "secondary");
                break;
            case "secondary":
                ServiceBusRegenerateAccessKeyContent regeneratePrimaryAccessKeyContent = new(ServiceBusAccessKeyType.PrimaryKey);
                keys = await existingRule.RegenerateKeysAsync(regeneratePrimaryAccessKeyContent, cancellationToken);
                result = keys.PrimaryConnectionString;
                context.SetValue("currentKey", "primary");
                break;
            default:
                throw new InvalidOperationException($"Unexpected 'currentKey' value '{currentKey}'.");
        }


        return new SecretData(result, DateTimeOffset.MaxValue, _clock.UtcNow.AddMonths(6));
    }
}
