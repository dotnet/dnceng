
using Azure;
using Azure.Core;
using Azure.ResourceManager.ServiceBus;
using Azure.ResourceManager.ServiceBus.Models;
using Azure.Security.KeyVault.Secrets;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager.Tests
{
    [TestFixture]
    [Category("PostDeployment")]
    public class ServiceBusTests : ScenarioTestsBase
    {
        const string AccessPolicySufix = "-access-policy";
        const string Namespace = "servicebussecretstest";
        const string ServiceBusNamePrefix = "sb";

        readonly string Manifest = @$"storageLocation:
  type: azure-key-vault
  parameters:
    name: {KeyVaultName}
    subscription: {SubscriptionId}
secrets:
  {ServiceBusNamePrefix}{{0}}:
    type: service-bus-connection-string
    owner: scenarioTests
    description: service bus connection string
    parameters:
      Subscription: {SubscriptionId}
      ResourceGroup: {ResourceGroup}
      Namespace: {Namespace}      
      Permissions: l
  ";

        [Test]
        public async Task NewConnectionStringSecretTest()
        {
            string nameSuffix = Guid.NewGuid().ToString("N");
            string connectionStringSecretName = ServiceBusNamePrefix + nameSuffix;
            string manifest = string.Format(Manifest, nameSuffix);

            await ExecuteSynchronizeCommand(manifest);

            SecretClient client = GetSecretClient();
            Response<KeyVaultSecret> connectionStringSecret = await client.GetSecretAsync(connectionStringSecretName);
            HashSet<string> connectionStringAccessKeys = await GetAccessKeys(connectionStringSecretName);

            Assert.That(connectionStringAccessKeys, Contains.Item(connectionStringSecret.Value.Value));
        }

        [Test]
        public async Task RotateConnectionStringSecretTest()
        {
            string nameSuffix = Guid.NewGuid().ToString("N");
            string connectionStringSecretName = ServiceBusNamePrefix + nameSuffix;
            string manifest = string.Format(Manifest, nameSuffix);

            SecretClient client = GetSecretClient();

            await ExecuteSynchronizeCommand(manifest);

            HashSet<string> accessKeys = await GetAccessKeys(connectionStringSecretName);

            Response<KeyVaultSecret> connectionStringSecret = await client.GetSecretAsync(connectionStringSecretName);
            await UpdateNextRotationTagIntoPast(client, connectionStringSecret.Value);

            await ExecuteSynchronizeCommand(manifest);

            HashSet<string> accessKeysRotated = await GetAccessKeys(connectionStringSecretName);

            accessKeysRotated.ExceptWith(accessKeys);

            Assert.That(accessKeysRotated, Has.Count.EqualTo(1));

            connectionStringSecret = await client.GetSecretAsync(connectionStringSecretName);

            Assert.That(connectionStringSecret.Value.Value, Is.EqualTo(accessKeysRotated.First()));
        }

        [OneTimeTearDown]
        public async Task Cleanup()
        {
            ResourceIdentifier serviceBusNamespaceId = ServiceBusNamespaceResource.CreateResourceIdentifier(SubscriptionId, ResourceGroup, Namespace);
            IAsyncEnumerable<ServiceBusNamespaceAuthorizationRuleResource> rules = _armClient.GetServiceBusNamespaceResource(serviceBusNamespaceId).GetServiceBusNamespaceAuthorizationRules().GetAllAsync();

            await foreach (ServiceBusNamespaceAuthorizationRuleResource rule in rules)
            {
                
                if (rule.Data.Name.EndsWith(AccessPolicySufix))
                {
                    await rule.DeleteAsync(WaitUntil.Completed);
                }
            }

            await PurgeAllSecrets();
        }


        private async Task<HashSet<string>> GetAccessKeys(string secretName)
        {
            string authorizationRuleName = secretName + AccessPolicySufix;

            ResourceIdentifier ruleResourceId = ServiceBusNamespaceAuthorizationRuleResource.CreateResourceIdentifier(SubscriptionId, ResourceGroup, Namespace, authorizationRuleName);
            ServiceBusNamespaceAuthorizationRuleResource ruleResource = await _armClient.GetServiceBusNamespaceAuthorizationRuleResource(ruleResourceId).GetAsync();
            ServiceBusAccessKeys result = await ruleResource.GetKeysAsync();

            return new HashSet<string>(new[] { result.PrimaryConnectionString, result.SecondaryConnectionString });
        }
    }
}
