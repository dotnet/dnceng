// See https://aka.ms/new-console-template for more information

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Octokit;

using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

class Program
{
    static async Task Main(string[] args)
    {
        String keyVaultName = "DncengChatbotKV";
        var vaultUrl = "https://" +keyVaultName + ".vault.azure.net";
        var client = new SecretClient(vaultUri: new Uri(vaultUrl), credential: new DefaultAzureCredential());

        string githubToken = (await client.GetSecretAsync("githubToken")).Value.Value;
        string connectionString = (await client.GetSecretAsync("connectionString")).Value.Value;
        string containerName = "automationdemo";

        //Arcade Repo
        string owner = "dotnet";
        string repo = "arcade";
        Program program = new Program();
        await program.UploadMDFilesToBlob(githubToken, connectionString, owner, repo, containerName);

        owner = "dotnet";
        repo = "dnceng";
        await program.UploadMDFilesToBlob(githubToken, connectionString, owner, repo, containerName);

    }

    public async Task UploadMDFilesToBlob(string githubToken, string connectionString, string owner, string repo, string containerName)
    {
        // Initialize GitHub client
        var client = new GitHubClient(new ProductHeaderValue($"{repo}-md-uploader"));
        client.Credentials = new Credentials(githubToken);

        // Initialize Azure Blob Storage client
        BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
        BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);

        // Initialize a counter for the number of uploaded markdown files
        int uploadCount = 0;

        // Define the maximum number of pages to fetch (GitHub API limit is 1000 results)
        int maxPages = 10;

        // Fetch and upload markdown files from the repository using the GitHub API with pagination
        for (int page = 1; page <= maxPages; page++)
        {
            var request = new SearchCodeRequest("extension:md repo:" + owner + "/" + repo)
            {
                PerPage = 100,
                Page = page
            };
            var result = await client.Search.SearchCode(request);

            if (result.Items == null || !result.Items.Any())
            {
                // No more results, break out of the loop
                break;
            }

            // Upload each markdown file to Azure Blob Storage
            foreach (var file in result.Items)
            {
                // Define the new path with the Arcade/dnceng directory
                string newPath = $"{repo}/{file.Path}";

                var blobClient = containerClient.GetBlobClient(newPath);
                var rawContent = await client.Repository.Content.GetRawContent(owner, repo, file.Path);
                using var ms = new MemoryStream(rawContent);
                await blobClient.UploadAsync(ms, overwrite: true);
                // Increment the counter after each successful upload
                uploadCount++;
                Console.WriteLine($"Uploaded {file.Path}");
            }
        }

        // Print the total number of markdown files uploaded
        Console.WriteLine($"Uploaded {uploadCount} markdown files to the {containerName} container in the {repo} repository.");
    }
}
