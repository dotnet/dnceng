using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Octokit;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

public class Program
{
    // Method to retrieve a list of BlobItems from a given BlobContainerClient
    public async Task<List<BlobItem>> GetBlobItemsAsync(BlobContainerClient containerClient)
    {
        // List to hold the retrieved blob items
        var blobItems = new List<BlobItem>();

        // Asynchronously enumerate through the blobs in the container
        await foreach (var blobItem in containerClient.GetBlobsAsync())
        {
            blobItems.Add(blobItem); // Add each blob item to the list
        }

        return blobItems; // Return the list of blob items
    }

    // Main entry point of the application
    static async Task Main(string[] args)
    {
        try
        {
            // Define the Key Vault name and URL
            String keyVaultName = "DncengChatbotKV";
            var vaultUrl = $"https://{keyVaultName}.vault.azure.net";
            // Create a SecretClient to interact with Azure Key Vault
            var client = new SecretClient(vaultUri: new Uri(vaultUrl), credential: new DefaultAzureCredential());

            // Retrieve GitHub token and Azure Blob connection string from Key Vault
            string githubToken = (await client.GetSecretAsync("githubToken")).Value.Value;
            string connectionString = (await client.GetSecretAsync("connectionString")).Value.Value;
            string containerName = "automationdemo"; // Name of the container where files will be uploaded

            // Initialize the Program instance
            Program program = new Program();

            // Upload Markdown files from the "arcade" repository
            await program.UploadMDFilesToBlob(githubToken, connectionString, "dotnet", "arcade", containerName);

            // Upload Markdown files from the "dnceng" repository
            await program.UploadMDFilesToBlob(githubToken, connectionString, "dotnet", "dnceng", containerName);
        }
        catch (Exception ex)
        {
            // Handle or log any exception that occurs during execution
            Console.WriteLine($"An error occurred: {ex.Message}");
        }
    }

    // Method to upload Markdown files from a GitHub repository to Azure Blob Storage
    public async Task UploadMDFilesToBlob(string githubToken, string connectionString, string owner, string repo, string containerName)
    {
        try
        {
            // Initialize GitHub client with provided token
            var client = new GitHubClient(new ProductHeaderValue($"{repo}-md-uploader"));
            client.Credentials = new Credentials(githubToken);

            // Get repository information and contents
            var repository = await client.Repository.Get(owner, repo);
            var files = await client.Repository.Content.GetAllContents(owner, repo);

            // Additional logic for uploading files would go here

        }
        catch (Exception ex)
        {
            // Handle exceptions related to GitHub API or other issues
            Console.WriteLine($"An error occurred: {ex.Message}");
            throw; // Rethrow to ensure that exceptions are visible in tests
        }

        try
        {
            // Initialize GitHub client for the second time (consider refactoring to avoid redundancy)
            var client = new GitHubClient(new ProductHeaderValue($"{repo}-md-uploader"));
            client.Credentials = new Credentials(githubToken);

            // Initialize Azure Blob Storage client
            BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);

            // Initialize a counter to track the number of uploaded files
            int uploadCount = 0;

            // Define the maximum number of pages for pagination (GitHub API limit)
            int maxPages = 10;

            // Loop through pages of results from GitHub API
            for (int page = 1; page <= maxPages; page++)
            {
                // Create a request to search for Markdown files in the repository
                var request = new SearchCodeRequest("extension:md repo:" + owner + "/" + repo)
                {
                    PerPage = 100,
                    Page = page
                };
                var result = await client.Search.SearchCode(request);

                if (result.Items == null || !result.Items.Any())
                {
                    // Exit loop if no more items are returned
                    break;
                }

                // Upload each Markdown file to Azure Blob Storage
                foreach (var file in result.Items)
                {
                    // Define the new path in the container
                    string newPath = $"{repo}/{file.Path}";

                    // Create a blob client and upload the file
                    var blobClient = containerClient.GetBlobClient(newPath);
                    var rawContent = await client.Repository.Content.GetRawContent(owner, repo, file.Path);
                    using var ms = new MemoryStream(rawContent);
                    await blobClient.UploadAsync(ms, overwrite: true);

                    // Increment the counter after each successful upload
                    uploadCount++;
                    Console.WriteLine($"Uploaded {repo}/{file.Path}");
                }
            }

            // Print the total number of Markdown files uploaded
            Console.WriteLine($"Uploaded {uploadCount} markdown files to the {containerName} container from the {repo} repository.");
        }
        catch (Exception ex)
        {
            // Handle exceptions related to Azure Blob Storage operations
            Console.WriteLine($"An error occurred while uploading files: {ex.Message}");
            throw; // Rethrow to ensure that exceptions are visible in tests
        }
    }
}
