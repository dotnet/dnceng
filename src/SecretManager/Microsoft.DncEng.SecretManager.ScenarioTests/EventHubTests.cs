
using Azure;
using Azure.Core;
using Azure.ResourceManager.EventHubs;
using Azure.ResourceManager.EventHubs.Models;
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
        [Ignore("Disabled - migrating to Managed Identity authentication, to be deleted after verification")]
        public async Task NewConnectionStringSecretTest()
        {
            string nameSuffix = Guid.NewGuid().ToString("N");
            string connectionStringSecretName = EventHubNamePrefix + nameSuffix;
            string manifest = string.Format(Manifest, nameSuffix);

            await ExecuteSynchronizeCommand(manifest);

            SecretClient client = GetSecretClient();
            Response<KeyVaultSecret> connectionStringSecret = await client.GetSecretAsync(connectionStringSecretName);
            HashSet<string> connectionStringAccessKeys = await GetAccessKeys(connectionStringSecretName);

            Assert.That(connectionStringAccessKeys, Contains.Item(connectionStringSecret.Value.Value));
        }

        [Test]
        [Ignore("Disabled - migrating to Managed Identity authentication, to be deleted after verification")]
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

            Assert.That(accessKeysRotated, Has.Count.EqualTo(1));
            connectionStringSecret = await client.GetSecretAsync(connectionStringSecretName);
            Assert.That(connectionStringSecret.Value.Value, Is.EqualTo(accessKeysRotated.First()));
        }

        [OneTimeTearDown]
        public async Task Cleanup()
        {
            ResourceIdentifier eventHubResourceId = EventHubResource.CreateResourceIdentifier(SubscriptionId, ResourceGroup, Namespace, Name);
            EventHubResource eventHub = _armClient.GetEventHubResource(eventHubResourceId);
            IAsyncEnumerable<EventHubAuthorizationRuleResource> rules = eventHub.GetEventHubAuthorizationRules().GetAllAsync();

            await foreach (EventHubAuthorizationRuleResource rule in rules)
            {
                await rule.DeleteAsync(WaitUntil.Completed);
            }

            await PurgeAllSecrets();
        }

        private async Task<HashSet<string>> GetAccessKeys(string secretName)
        {
            string accessPolicyName = secretName + "-access-policy";

            ResourceIdentifier ruleResourceId = EventHubAuthorizationRuleResource.CreateResourceIdentifier(SubscriptionId, ResourceGroup, Namespace, Name, accessPolicyName);
            EventHubAuthorizationRuleResource ruleResource = await _armClient.GetEventHubAuthorizationRuleResource(ruleResourceId).GetAsync();
            EventHubsAccessKeys result = await ruleResource.GetKeysAsync();

            return new HashSet<string>(new[] { result.PrimaryConnectionString, result.SecondaryConnectionString });
        }
    }
}
