using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Security.KeyVault.Secrets;
using Azure.Identity;
using Octokit;

namespace UploadDocsTests
{
    [TestFixture]
    public class ProgramTests
    {
        private const string KeyVaultName = "DncengChatbotKV";
        private const string accountName = "explorerstestdata";
        private const string ContainerName = "testcontainer"; // Use a test container
        private const string EmptyContainerName = "test-emptycontainer"; // Use an empty test container

        // Method to create a SecretClient to interact with Azure Key Vault
        private static SecretClient GetKeyVaultClient()
        {
            var vaultUrl = $"https://{KeyVaultName}.vault.azure.net";
            return new SecretClient(new Uri(vaultUrl), new DefaultAzureCredential());
        }

        // Method to retrieve a secret value from Key Vault
        private static async Task<string> GetSecretAsync(string secretName)
        {
            var client = GetKeyVaultClient();
            return (await client.GetSecretAsync(secretName)).Value.Value;
        }

        // Method to get the list of blobs from a given container client
        private static async Task<List<BlobItem>> GetBlobItemsAsync(BlobContainerClient containerClient)
        {
            var blobItems = new List<BlobItem>();

            await foreach (var blobItem in containerClient.GetBlobsAsync())
            {
                blobItems.Add(blobItem);
            }

            return blobItems;
        }

        // Method to check if a Blob container exists
        private static async Task<bool> ContainerExistsAsync(BlobServiceClient blobServiceClient, string containerName)
        {
            var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            return await containerClient.ExistsAsync();
        }

        [SetUp]
        public async Task SetUp()
        {
            // Arrange
            string connectionString = await GetSecretAsync("connectionString");
            var blobServiceClient = new BlobServiceClient(connectionString);

            // Validate that both containers exist before running tests
            Assert.IsTrue(await ContainerExistsAsync(blobServiceClient, ContainerName), $"Container '{ContainerName}' does not exist.");
            Assert.IsTrue(await ContainerExistsAsync(blobServiceClient, EmptyContainerName), $"Container '{EmptyContainerName}' does not exist.");
        }

        [Test]
        public async Task UploadMDFilesToBlob_UploadsFilesSuccessfully()
        {
            // Arrange
            string githubToken = await GetSecretAsync("githubToken");
            string connectionString = await GetSecretAsync("connectionString");
            string owner = "dotnet";
            string repo = ".github"; // Use a test repository
            var program = new Program();

            // Act
            await program.UploadMDFilesToBlob(githubToken, connectionString, owner, repo, ContainerName, accountName);

            // Assert
            var blobServiceClient = new BlobServiceClient(connectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);
            var blobList = await GetBlobItemsAsync(containerClient);

            Assert.IsTrue(blobList.Any(b => b.Name.EndsWith(".md")), "No Markdown files were uploaded.");
        }

        [Test]
        public async Task UploadMDFilesToBlob_HandlesMultipleRepositories()
        {
            // Arrange
            string githubToken = await GetSecretAsync("githubToken");
            string connectionString = await GetSecretAsync("connectionString");
            string[] repositories = { "actions-create-pull-request", ".github" };
            var program = new Program();

            // Act
            foreach (var repo in repositories)
            {
                await program.UploadMDFilesToBlob(githubToken, connectionString, "dotnet", repo, ContainerName, accountName);
            }

            // Assert
            var blobServiceClient = new BlobServiceClient(connectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);
            var blobList = await GetBlobItemsAsync(containerClient);

            Assert.IsTrue(blobList.Any(b => b.Name.EndsWith(".md")), "No Markdown files were uploaded.");
        }

        [Test]
        public async Task UploadMDFilesToBlob_HandlesErrorFromGitHub()
        {
            // Arrange
            string githubToken = await GetSecretAsync("githubToken");
            string connectionString = await GetSecretAsync("connectionString");
            string owner = "dotnet";
            string repo = "invalid-repo"; // Use a repository that will cause an error
            var program = new Program();

            // Act & Assert
            Assert.ThrowsAsync<Octokit.NotFoundException>(async () =>
            {
                await program.UploadMDFilesToBlob(githubToken, connectionString, owner, repo, ContainerName, accountName);
            });
        }

        [Test]
        public async Task UploadMDFilesToBlob_HandlesErrorFromAzureBlob()
        {
            // Arrange
            var invalidConnectionString = "invalid-connection-string";
            var program = new Program();

            // Act & Assert
            var ex = Assert.ThrowsAsync<Octokit.AuthorizationException>(async () =>
            {
                await program.UploadMDFilesToBlob("githubToken", invalidConnectionString, "owner", "repo", "containerName", accountName);
            });

            // Verify that the exception message contains the expected text
            Assert.That(ex.Message, Does.Contain("Bad credentials"));
        }

        [Test]
        public async Task UploadMDFilesToBlob_HandlesNoFilesFound()
        {
            // Arrange
            string githubToken = await GetSecretAsync("githubToken");
            string connectionString = await GetSecretAsync("connectionString");
            string owner = "kcesaire";
            string repo = "AutomationDemo"; // Use a repository known to have no Markdown files
            var program = new Program();

            // Act
            await program.UploadMDFilesToBlob(githubToken, connectionString, owner, repo, EmptyContainerName, accountName);

            // Assert
            var blobServiceClient = new BlobServiceClient(connectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(EmptyContainerName);
            var blobList = await GetBlobItemsAsync(containerClient);

            Assert.IsEmpty(blobList.Where(b => b.Name.EndsWith(".md")), "Unexpected Markdown files were uploaded.");
        }
    }
}
