using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Octokit;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure;

public class Program
{
    private const int GitHubLimitMaxRecords = 1000; // Define a constant for the maximum number of records



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
            Console.WriteLine("Starting the program");
            String keyVaultName = "DncengChatbotKV";
            var vaultUrl = $"https://{keyVaultName}.vault.azure.net";
            
            var options = new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = "ed9b4931-f097-4f9b-ae95-cec8c7f06dd8",
                TenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47"
            };
            var client = new SecretClient(vaultUri: new Uri(vaultUrl), credential: new DefaultAzureCredential(options));

            Console.WriteLine("Getting secret github");
            string githubToken = (await client.GetSecretAsync("githubToken")).Value.Value;
            string connectionString = (await client.GetSecretAsync("connectionString")).Value.Value;
            string containerName = "automationdemo";
            string accountName = "explorerstestdata";

            Console.WriteLine("Initialize program instance");
            Program program = new Program();

            Console.WriteLine("Upload files from Arcade Repo");
            await program.UploadMDFilesToBlob(githubToken, connectionString, "dotnet", "arcade", containerName, accountName);

            Console.WriteLine("Upload files from dnceng Repo");
            await program.UploadMDFilesToBlob(githubToken, connectionString, "dotnet", "dnceng", containerName, accountName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            throw;
        }
    }

    public async Task UploadMDFilesToBlob(string githubToken, string connectionString, string owner, string repo, string containerName, string accountName)
    {
        try
        {
            Console.WriteLine("Initialize GitHub client");
            var githubClient = new GitHubClient(new ProductHeaderValue($"{repo}-md-uploader"))
            {
                Credentials = new Credentials(githubToken)
            };

            Console.WriteLine("Initialize Azure Blob Storage client");
            var options = new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = "ed9b4931-f097-4f9b-ae95-cec8c7f06dd8",
                TenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47"
            };
            var containerEndpoint = $"https://{accountName}.blob.core.windows.net/{containerName}";
            var containerClient = new BlobContainerClient(new Uri(containerEndpoint), new DefaultAzureCredential(options));

            Console.WriteLine("Retrieve existing blobs");
            var existingBlobs = await GetBlobItemsAsync(containerClient);
            var existingBlobPaths = new HashSet<string>(existingBlobs.Select(b => b.Name));

            int uploadedFilesCount = 0;
            

            int recordsPerPage = 100;
            int page = 1;
            int totalResults = 0;
            var newBlobPaths = new HashSet<string>();

            SearchCodeResult result = null;

            do
            {
                int recordsToRetrieve = Math.Min(recordsPerPage, GitHubLimitMaxRecords - totalResults);

                var request = new SearchCodeRequest($"extension:md repo:{owner}/{repo}")
                {
                    PerPage = recordsToRetrieve,
                    Page = page
                };
                
                Console.WriteLine("SearchCodeRequest");
                result = await githubClient.Search.SearchCode(request);

                if (result.Items == null || !result.Items.Any())
                {
                    break;
                }

                Console.WriteLine("Upload each Markdown file to Azure Blob Storage");
                foreach (var file in result.Items)
                {
                    string newPath = $"{repo}/{file.Path}";
                    newBlobPaths.Add(newPath);

                    var blobClient = containerClient.GetBlobClient(newPath);
                    var rawContent = await githubClient.Repository.Content.GetRawContent(owner, repo, file.Path);

                    using var ms = new MemoryStream(rawContent);
                    await blobClient.UploadAsync(ms, overwrite: true);

                    uploadedFilesCount++;
                    Console.WriteLine($"Uploaded {repo}/{file.Path}");
                }

                totalResults += result.Items.Count;
                page++;

            } while (totalResults < GitHubLimitMaxRecords && result.Items.Any());

            // NOTE: Code for deleting files from blob storage that were deleted in GitHub has been removed
            // due to issues with file mismatches and unintended deletions.

            // Report results
            Console.WriteLine($"Total files uploaded: {uploadedFilesCount}");
            Console.WriteLine($"Uploaded files for repository {repo}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            throw;
        }
    }





}
