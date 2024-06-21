using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.Security.KeyVault.Secrets;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.DncEng.SecretManager.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.DncEng.SecretManager.Tests
{
    public class ScenarioTestsBase
    {
        protected const string ResourceGroup = "secret-manager-scenario-tests";
        protected const string SubscriptionId = "a4fc5514-21a9-4296-bfaf-5c7ee7fa35d1";
        protected const string NextRotationOnTag = "next-rotation-on";
        protected const string KeyVaultName = "SecretManagerTestsKv";

        // Expect credentials for the Service Principal used in these tests to be set
        // in enviornment variables.
        private readonly TokenCredential _tokenCredential = new DefaultAzureCredential(
            new DefaultAzureCredentialOptions { 
                ManagedIdentityClientId = Environment.GetEnvironmentVariable("ARM_CLIENT_ID")
            });
            
        protected async Task ExecuteSynchronizeCommand(string manifest)
        {
            ServiceCollection services = new ServiceCollection();
            services.AddSingleton<SynchronizeCommand>();

            Program program = new Program();
            program.ConfigureServiceCollection(services);

            // Use environment credentials
            services.RemoveAll<ITokenCredentialProvider>();
            services.AddSingleton<ITokenCredentialProvider, WrappedTokenProvider>(_ => new WrappedTokenProvider(_tokenCredential));

            // Replace the console with a test console so that we don't get a bunch of errors/warnings
            // the command line when running these tests
            services.RemoveAll<IConsole>();
            services.RemoveAll<IConsoleBackend>();
            services.AddSingleton<IConsole, TestConsole>();

            ServiceProvider provider = services.BuildServiceProvider();
            GlobalCommand globalCommand = ActivatorUtilities.CreateInstance<GlobalCommand>(provider);
            CommandOptions commandOptions = provider.GetService<ICommandOptions>() as CommandOptions;
            commandOptions.RegisterOptions(globalCommand);

            SynchronizeCommand command = provider.GetRequiredService<SynchronizeCommand>();
            CancellationTokenSource cts = new CancellationTokenSource();

            string manifestFile = null;
            try
            {
                manifestFile = Path.GetTempFileName();
                await File.WriteAllTextAsync(manifestFile, manifest, cts.Token);

                command.HandlePositionalArguments(new List<string> { manifestFile });

                await command.RunAsync(cts.Token);
            }
            finally
            {
                if (manifestFile != null)
                    File.Delete(manifestFile);
            }
        }

        protected SecretClient GetSecretClient()
        {
            var client = new SecretClient(
                new Uri($"https://{KeyVaultName}.vault.azure.net/"),
                _tokenCredential);

            return client;
        }

        protected static async Task UpdateNextRotationTag(SecretClient client, KeyVaultSecret secret, DateTime date)
        {
            secret.Properties.Tags[NextRotationOnTag] = date.ToString("O");
            await client.UpdateSecretPropertiesAsync(secret.Properties);
        }

        protected static async Task UpdateNextRotationTagIntoPast(SecretClient client, KeyVaultSecret secret)
        {
            await UpdateNextRotationTag(client, secret, DateTime.Today.AddDays(-1));
        }

        protected static async Task UpdateNextRotationTagIntoFuture(SecretClient client, KeyVaultSecret secret)
        {
            await UpdateNextRotationTag(client, secret, DateTime.Today.AddDays(15));
        }

        protected async Task PurgeAllSecrets()
        {
            var deleteOperations = new List<DeleteSecretOperation>();
            SecretClient client = GetSecretClient();

            await foreach (var secret in client.GetPropertiesOfSecretsAsync())
            {
                var operation = await client.StartDeleteSecretAsync(secret.Name);
                deleteOperations.Add(operation);
            }

            foreach (var deleteOperation in deleteOperations)
            {
                await deleteOperation.WaitForCompletionAsync();
                await client.PurgeDeletedSecretAsync(deleteOperation.Value.Name);
            }
        }
    }
}
