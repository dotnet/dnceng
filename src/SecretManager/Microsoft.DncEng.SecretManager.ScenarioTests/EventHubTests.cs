
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.EventHubs;
using Azure.ResourceManager.ServiceBus.Models;
using Azure.ResourceManager.ServiceBus;
using Azure.Security.KeyVault.Secrets;
using NUnit.Framework;
using Azure.ResourceManager.EventHubs.Models;

namespace Microsoft.DncEng.SecretManager.Tests
{
    [TestFixture]
    [Category("PostDeployment")]
    public class EventHubTests : ScenarioTestsBase
    {
        const string Name = "test-event-hub";
        const string Namespace = "EventHubSecretsTest";
        const string EventHubNamePrefix = "event-hub-connection-string";

        readonly string Manifest = @$"storageLocation:
  type: azure-key-vault
  parameters:
    name: {KeyVaultName}
    subscription: {SubscriptionId}
secrets:
  {EventHubNamePrefix}{{0}}:
    type: event-hub-connection-string
    owner: scenarioTests
    description: storage connection string
    parameters:
      Subscription: {SubscriptionId}
      ResourceGroup: {ResourceGroup}
      Namespace: {Namespace}
      Name: {Name}
      Permissions: l
  ";

        [Test]
        public async Task NewConnectionStringSecretTest()
        {
            string nameSuffix = Guid.NewGuid().ToString("N");
            string connectionStringSecretName = EventHubNamePrefix + nameSuffix;
            string manifest = string.Format(Manifest, nameSuffix);

            await ExecuteSynchronizeCommand(manifest);

            SecretClient client = GetSecretClient();
            Response<KeyVaultSecret> connectionStringSecret = await client.GetSecretAsync(connectionStringSecretName);
            HashSet<string> connectionStringAccessKeys = await GetAccessKeys(connectionStringSecretName);

            Assert.IsTrue(connectionStringAccessKeys.Contains(connectionStringSecret.Value.Value));
        }

        [Test]
        public async Task RotateConnectionStringSecretTest()
        {
            string nameSuffix = Guid.NewGuid().ToString("N");
            string connectionStringSecretName = EventHubNamePrefix + nameSuffix;
            string manifest = string.Format(Manifest, nameSuffix);

            SecretClient client = GetSecretClient();

            await ExecuteSynchronizeCommand(manifest);

            HashSet<string> accessKeys = await GetAccessKeys(connectionStringSecretName);
            Response<KeyVaultSecret> connectionStringSecret = await client.GetSecretAsync(connectionStringSecretName);
            await UpdateNextRotationTagIntoPast(client, connectionStringSecret.Value);

            await ExecuteSynchronizeCommand(manifest);

            HashSet<string> accessKeysRotated = await GetAccessKeys(connectionStringSecretName);

            accessKeysRotated.ExceptWith(accessKeys);

            Assert.AreEqual(1, accessKeysRotated.Count);
            connectionStringSecret = await client.GetSecretAsync(connectionStringSecretName);
            Assert.AreEqual(connectionStringSecret.Value.Value, accessKeysRotated.First());
        }

        [OneTimeTearDown]
        public async Task Cleanup()
        {
            ArmClient client = new(_tokenCredential, SubscriptionId);

            ResourceIdentifier eventHubResourceId = EventHubResource.CreateResourceIdentifier(SubscriptionId, ResourceGroup, Namespace, Name);
            EventHubResource eventHub = client.GetEventHubResource(eventHubResourceId);
            IAsyncEnumerable<EventHubAuthorizationRuleResource> rules = eventHub.GetEventHubAuthorizationRules().GetAllAsync();

            await foreach (EventHubAuthorizationRuleResource rule in rules)
            {
                await rule.DeleteAsync(WaitUntil.Completed);
            }

            await PurgeAllSecrets();
        }

        private async Task<HashSet<string>> GetAccessKeys(string secretName)
        {
            ArmClient client = new(_tokenCredential, SubscriptionId);
            string accessPolicyName = secretName + "-access-policy";

            ResourceIdentifier ruleResourceId = EventHubAuthorizationRuleResource.CreateResourceIdentifier(SubscriptionId, ResourceGroup, Namespace, Name, accessPolicyName);
            EventHubAuthorizationRuleResource ruleResource = await client.GetEventHubAuthorizationRuleResource(ruleResourceId).GetAsync();
            EventHubsAccessKeys result = await ruleResource.GetKeysAsync();

            return new HashSet<string>(new[] { result.PrimaryConnectionString, result.SecondaryConnectionString });
        }
    }
}
