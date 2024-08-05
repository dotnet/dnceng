using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using RolloutScorer;
using RolloutScorer.Models;

namespace RolloutScorerAzureFunction;

public static class RolloutScorerFunction
{
    private const int ScoringBufferInDays = 2;

    [FunctionName("RolloutScorerFunction")]
    public static async Task Run([TimerTrigger("0 0 0 * * *")]TimerInfo myTimer, ILogger log)
    {
        DefaultAzureCredential tokenProvider = new();

        string deploymentEnvironment = Environment.GetEnvironmentVariable("DeploymentEnvironment") ?? "Staging";
        log.LogInformation($"INFO: Deployment Environment: {deploymentEnvironment}");

        log.LogInformation("INFO: Getting scorecard storage account key and deployment table's SAS URI from KeyVault...");
        SecretClient engKeyVaultClient = new(new Uri(Utilities.KeyVaultUri), tokenProvider);

        log.LogInformation("INFO: Getting cloud tables...");
        TableClient scorecardsTable = Utilities.GetTableClient(ScorecardsStorageAccount.Name, ScorecardsStorageAccount.ScorecardsTableName);
        TableClient deploymentsTable = Utilities.GetTableClient(DeploymentsStorageAccount.Name, DeploymentsStorageAccount.DeploymentsTableName);

        List<ScorecardEntity> scorecardEntries = (await GetAllTableEntriesAsync<ScorecardEntity>(scorecardsTable))
            .OrderBy(s => DateTimeOffset.ParseExact(s.PartitionKey, "yyyy-MM-dd", null)).ToList();
        List<AnnotationEntity> deploymentEntries =
            await GetAllTableEntriesAsync<AnnotationEntity>(deploymentsTable);
        deploymentEntries.Sort((x, y) => (x.Ended ?? DateTimeOffset.MaxValue).CompareTo(y.Ended ?? DateTimeOffset.MaxValue));
        log.LogInformation($"INFO: Found {scorecardEntries?.Count ?? -1} scorecard table entries and {deploymentEntries?.Count ?? -1} deployment table entries." +
                           $"(-1 indicates that null was returned.)");

        // The deployments we care about are ones that occurred after the last scorecard
        IEnumerable<AnnotationEntity> relevantDeployments = deploymentEntries.Where(d =>
            (d.Ended ?? DateTimeOffset.MaxValue) > scorecardEntries.Last().Timestamp?.AddDays(ScoringBufferInDays));
        log.LogInformation($"INFO: Found {relevantDeployments?.Count() ?? -1} relevant deployments (deployments which occurred " +
                           $"after the last scorecard). (-1 indicates that null was returned.)");

        if (relevantDeployments.Count() > 0)
        {
            log.LogInformation($"INFO: Checking to see if the most recent deployment occurred more than {ScoringBufferInDays} days ago...");
            // We have only want to score if the buffer period has elapsed since the last deployment
            // Alternatively, if too much time has elapsed since that deployment started, it means there's the BAD BUG and we should just assume this rollout completed
            if ((relevantDeployments.Last().Ended ?? DateTimeOffset.MaxValue) < DateTimeOffset.UtcNow - TimeSpan.FromDays(ScoringBufferInDays)
                || ((relevantDeployments.Last().Started ?? DateTimeOffset.MaxValue) < DateTimeOffset.UtcNow - TimeSpan.FromDays(ScoringBufferInDays + 1) && relevantDeployments.Last().Ended is null))
            {
                var scorecards = new List<Scorecard>();

                log.LogInformation("INFO: Rollouts will be scored. Fetching GitHub PAT...");
                KeyVaultSecret githubPat = await engKeyVaultClient.GetSecretAsync(Utilities.GitHubPatSecretName);

                // We'll score the deployments by service
                foreach (var deploymentGroup in relevantDeployments.GroupBy(d => d.PartitionKey))
                {
                    foreach (var deployment in deploymentGroup)
                    {
                        if (deployment.Ended is null)
                        {
                            deployment.Ended = DateTimeOffset.UtcNow;
                            await deploymentsTable.UpdateEntityAsync(deployment,ETag.All, TableUpdateMode.Replace);
                        }
                    }

                    log.LogInformation($"INFO: Scoring {deploymentGroup?.Count() ?? -1} rollouts for repo '{deploymentGroup.Key}'");
                    RolloutScorer.RolloutScorer rolloutScorer = new()
                    {
                        Repo = deploymentGroup.Key,
                        RolloutStartDate = deploymentGroup.First().Started.GetValueOrDefault().Date,
                        RolloutWeightConfig = StandardConfig.DefaultConfig.RolloutWeightConfig,
                        GithubConfig = StandardConfig.DefaultConfig.GithubConfig,
                        Log = log,
                    };
                    log.LogInformation($"INFO: Finding repo config for {rolloutScorer.Repo}...");
                    rolloutScorer.RepoConfig = StandardConfig.DefaultConfig.RepoConfigs
                        .Find(r => r.Repo == rolloutScorer.Repo);
                    log.LogInformation($"INFO: Repo config: {rolloutScorer.RepoConfig.Repo}");
                    log.LogInformation($"INFO: Finding AzDO config for {rolloutScorer.RepoConfig.AzdoInstance}...");
                    rolloutScorer.AzdoConfig = StandardConfig.DefaultConfig.AzdoInstanceConfigs
                        .Find(a => a.Name == rolloutScorer.RepoConfig.AzdoInstance);

                    log.LogInformation($"INFO: Fetching AzDO PAT from KeyVault...");
                    SecretClient azdoConfigVaultClient = new SecretClient(new Uri(rolloutScorer.AzdoConfig.KeyVaultUri), tokenProvider);
                    KeyVaultSecret azdoPatSecret = await azdoConfigVaultClient.GetSecretAsync(rolloutScorer.AzdoConfig.PatSecretName);
                    rolloutScorer.SetupHttpClient(azdoPatSecret.Value);
                    rolloutScorer.SetupGithubClient(githubPat.Value);

                    log.LogInformation($"INFO: Attempting to initialize RolloutScorer...");
                    try
                    {
                        await rolloutScorer.InitAsync();
                    }
                    catch (ArgumentException e)
                    {
                        log.LogError($"ERROR: Error while processing {rolloutScorer.RolloutStartDate} rollout of {rolloutScorer.Repo}.");
                        log.LogError($"ERROR: {e.Message}");
                        continue;
                    }

                    log.LogInformation($"INFO: Creating rollout scorecard...");
                    scorecards.Add(await Scorecard.CreateScorecardAsync(rolloutScorer));
                    log.LogInformation($"INFO: Successfully created scorecard for {rolloutScorer.RolloutStartDate?.Date} rollout of {rolloutScorer.Repo}.");
                }

                log.LogInformation($"INFO: Uploading results for {string.Join(", ", scorecards.Select(s => s.Repo))}");
                await RolloutUploader.UploadResultsAsync(scorecards, Utilities.GetGithubClient(githubPat.Value), StandardConfig.DefaultConfig.GithubConfig, skipPr: deploymentEnvironment != "Production");
            }
            else
            {
                log.LogInformation(relevantDeployments.Last().Ended.HasValue ? $"INFO: Most recent rollout occurred less than two days ago " +
                                                                               $"({relevantDeployments.Last().PartitionKey} on {relevantDeployments.Last().Ended.Value}); waiting to score." :
                    $"Most recent rollout ({relevantDeployments.Last().PartitionKey}) is still in progress.");
            }
        }
        else
        {
            log.LogInformation($"INFO: Found no rollouts which occurred after last recorded rollout " +
                               $"({(scorecardEntries.Count > 0 ? $"date {scorecardEntries.Last().PartitionKey}" : "no rollouts in table")})");
        }
    }

    private static async Task<List<T>> GetAllTableEntriesAsync<T>(TableClient table) where T : class, ITableEntity, new()
    {
        List<T> items = new List<T>();
      
        await foreach (Page<T> page in table.QueryAsync<T>().AsPages())
        {
            items.AddRange(page.Values);
        }
        return items;
    }
}