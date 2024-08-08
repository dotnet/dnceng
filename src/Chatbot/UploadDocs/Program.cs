using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Octokit;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure;

public class Program
{
    public async Task<List<BlobItem>> GetBlobItemsAsync(BlobContainerClient containerClient)
    {
        var blobItems = new List<BlobItem>();

        await foreach (var blobItem in containerClient.GetBlobsAsync())
        {
            blobItems.Add(blobItem);
        }

        return blobItems;
    }

    static async Task Main(string[] args)
    {
        
        try
        {
            String keyVaultName = "DncengChatbotKV";
            var vaultUrl = $"https://{keyVaultName}.vault.azure.net";
            var client = new SecretClient(vaultUri: new Uri(vaultUrl), credential: new DefaultAzureCredential());

            string githubToken = (await client.GetSecretAsync("githubToken")).Value.Value;
            string connectionString = (await client.GetSecretAsync("connectionString")).Value.Value;
            string containerName = "automationdemo";

            // Initialize program instance
            Program program = new Program();

            // Upload files from Arcade Repo
            await program.UploadMDFilesToBlob(githubToken, connectionString, "dotnet", "arcade", containerName);

            // Upload files from dnceng Repo
            await program.UploadMDFilesToBlob(githubToken, connectionString, "dotnet", "dnceng", containerName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            // Handle or log the exception as necessary
        }
    }

    public async Task UploadMDFilesToBlob(string githubToken, string connectionString, string owner, string repo, string containerName)
    {
        try
        {
            // Initialize GitHub client
            var githubClient = new GitHubClient(new ProductHeaderValue($"{repo}-md-uploader"));
            githubClient.Credentials = new Credentials(githubToken);

            // Initialize Azure Blob Storage client
            BlobServiceClient blobServiceClient = new BlobServiceClient(new DefaultAzureCredential());
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);

            // Retrieve existing blobs
            var existingBlobs = await GetBlobItemsAsync(containerClient);
            var existingBlobPaths = new HashSet<string>(existingBlobs.Select(b => b.Name));

            // Define the maximum number of pages for pagination (GitHub API limit)
            int maxPages = 10;
            var newBlobPaths = new HashSet<string>();

            // Loop through pages of results from GitHub API
            for (int page = 1; page <= maxPages; page++)
            {
                var request = new SearchCodeRequest("extension:md repo:" + owner + "/" + repo)
                {
                    PerPage = 100,
                    Page = page
                };
                var result = await githubClient.Search.SearchCode(request);

                if (result.Items == null || !result.Items.Any())
                {
                    break; // Exit loop if no more items are returned
                }

                // Upload each Markdown file to Azure Blob Storage
                foreach (var file in result.Items)
                {
                    string newPath = $"{repo}/{file.Path}";
                    newBlobPaths.Add(newPath);

                    var blobClient = containerClient.GetBlobClient(newPath);
                    var rawContent = await githubClient.Repository.Content.GetRawContent(owner, repo, file.Path);
                    using var ms = new MemoryStream(rawContent);
                    await blobClient.UploadAsync(ms, overwrite: true);

                    Console.WriteLine($"Uploaded {repo}/{file.Path}");
                }
            }

            // Delete blobs that are no longer present in the GitHub repository
            // Iterate through each existing blob path retrieved from the Azure Blob Storage container
            foreach (var existingBlobPath in existingBlobPaths)
            {
                // Check if the current blob path is not present in the list of new blob paths (i.e., it has been removed from the GitHub repository)
                if (!newBlobPaths.Contains(existingBlobPath))
                {
                    // Create a BlobClient for the current blob path, allowing operations like deletion on that specific blob
                    var blobClient = containerClient.GetBlobClient(existingBlobPath);

                    try
                    {
                        // Attempt to delete the blob if it exists. This method returns true if the blob was deleted or false if it did not exist
                        await blobClient.DeleteIfExistsAsync();

                        // Log the successful deletion of the blob
                        Console.WriteLine($"Deleted {existingBlobPath}");
                    }
                    catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.BlobNotFound)
                    {
                        // This block is executed if the deletion fails because the blob was not found (e.g., it was already deleted or never existed)
                        Console.WriteLine($"Blob not found for deletion: {existingBlobPath}");
                    }
                    catch (Exception ex)
                    {
                        // This block catches any other exceptions that might occur during the deletion process
                        // For example, network issues or permission problems might cause this exception
                        // log the error message to help diagnose the issue
                        Console.WriteLine($"Error deleting blob {existingBlobPath}: {ex.Message}");
                    }
                }
            }


            Console.WriteLine($"Uploaded and cleaned up files for repository {repo}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            throw; // Rethrow to ensure that exceptions are visible in tests
        }
    }



}
