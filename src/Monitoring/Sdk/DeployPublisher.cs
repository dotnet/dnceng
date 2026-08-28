// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.DotNet.Monitoring.Sdk;

public sealed class DeployPublisher : DeployToolBase, IDisposable
{
    private readonly string _keyVaultName;
    private readonly TokenCredential _tokenCredential;

    private readonly Lazy<SecretClient> _keyVault;
    private readonly string _environment;
    private readonly string _parameterFile;
    private readonly string _retirementDirectory;
    private readonly bool _allowDeletes;

    private SecretClient KeyVault => _keyVault.Value;

    public DeployPublisher(
        GrafanaClient grafanaClient,
        string keyVaultName,
        TokenCredential tokenCredential,
        string sourceTagValue,
        string dashboardDirectory,
        string datasourceDirectory,
        string notificationDirectory,
        string retirementDirectory,
        bool allowDeletes,
        string environment,
        string parametersFile,
        TaskLoggingHelper log) : base(
        grafanaClient, sourceTagValue, dashboardDirectory, datasourceDirectory, notificationDirectory, log)
    {
        _keyVaultName = keyVaultName;
        _tokenCredential = tokenCredential;
        _environment = environment;
        _retirementDirectory = retirementDirectory;
        _allowDeletes = allowDeletes;
        _keyVault = new Lazy<SecretClient>(GetKeyVaultClient);
        _parameterFile = parametersFile;
    }
        
    private string EnvironmentDatasourceDirectory => Path.Combine(DatasourceDirectory, _environment);
    private string EnvironmentNotificationDirectory => Path.Combine(NotificationDirectory, _environment);
    private string AlertRuleDirectory
    {
        get
        {
            string baseDir = Path.Combine(Path.GetDirectoryName(NotificationDirectory), "alertrules");
            string environmentSpecificDir = Path.Combine(baseDir, _environment);
            
            // If environment-specific folder exists, use it; otherwise fall back to base directory
            if (Directory.Exists(environmentSpecificDir))
            {
                return environmentSpecificDir;
            }
            
            return baseDir;
        }
    }

    public void Dispose()
    {
        // Nothing to dispose of
    }

    public async Task PostToGrafanaAsync()
    {
        await PostDatasourcesAsync().ConfigureAwait(false);

        await PostContactPointsAsync().ConfigureAwait(false);

        await PostAlertRulesAsync().ConfigureAwait(false);

        HashSet<string> knownDashboardUids = await PostDashboardsAsync().ConfigureAwait(false);

        await RetireResourcesAsync().ConfigureAwait(false);

        await SetHomeDashboardAsync().ConfigureAwait(false);

        await ClearExtraneousDashboardsAsync(knownDashboardUids).ConfigureAwait(false);

        await DeleteRetiredDashboardsAsync().ConfigureAwait(false);
    }

    private async Task RetireResourcesAsync()
    {
        string retirementPath = Path.Combine(_retirementDirectory, _environment + ".retirement.json");
        if (!File.Exists(retirementPath))
        {
            return;
        }

        JObject retirementPlan;
        using (var streamReader = new StreamReader(retirementPath))
        using (var jsonReader = new JsonTextReader(streamReader))
        {
            retirementPlan = await JObject.LoadAsync(jsonReader).ConfigureAwait(false);
        }

        string[] alertRuleUids = retirementPlan.Value<JArray>("alertRules")?
            .Values<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();
        string[] contactPointNames = retirementPlan.Value<JArray>("contactPoints")?
            .Values<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();

        if (!_allowDeletes)
        {
            Log.LogWarning(
                "Grafana retirement plan {0} is in report-only mode. Would delete {1} alert rule(s) and {2} contact point(s).",
                retirementPath,
                alertRuleUids.Length,
                contactPointNames.Length);
            return;
        }

        foreach (string uid in alertRuleUids)
        {
            bool deleted = await GrafanaClient.DeleteAlertRuleAsync(uid).ConfigureAwait(false);
            Log.LogMessage(
                MessageImportance.High,
                deleted ? "Deleted retired alert rule {0}." : "Retired alert rule {0} was already absent.",
                uid);
        }

        foreach (string name in contactPointNames)
        {
            if (await GrafanaClient.NotificationPolicyReferencesContactPointAsync(name).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    $"Grafana notification policy still references contact point '{name}'. Remove the route before deleting the contact point.");
            }

            int deleted = await GrafanaClient.DeleteContactPointsByNameAsync(name).ConfigureAwait(false);
            Log.LogMessage(
                MessageImportance.High,
                deleted == 0
                    ? "Retired contact point {0} was already absent."
                    : "Deleted {1} integration(s) for retired contact point {0}.",
                name,
                deleted);
        }
    }

    private async Task PostDatasourcesAsync()
    {
        foreach (string datasourcePath in Directory.GetFiles(EnvironmentDatasourceDirectory,
                     "*" + DatasourceExtension,
                     SearchOption.AllDirectories))
        {
            var name = GetNameFromDatasourceFile(Path.GetFileName(datasourcePath));
            JObject data;
            using (var sr = new StreamReader(datasourcePath))
            using (var jr = new JsonTextReader(sr))
            {
                data = await JObject.LoadAsync(jr).ConfigureAwait(false);
            }

            data["name"] = name;

            Log.LogMessage(MessageImportance.Normal, "Posting datasource {0}...", name);

            await ReplaceVaultAsync(data);

            await GrafanaClient.CreateDatasourceAsync(data).ConfigureAwait(false);
        }
    }

    private async Task PostNotificationsAsync()
    {
        foreach (string notificationPath in Directory.GetFiles(EnvironmentNotificationDirectory,
                     "*" + NotificationExtension,
                     SearchOption.AllDirectories))
        {
            string uid = GetUidFromNotificationFile(Path.GetFileName(notificationPath));

            JObject data;
            using (var sr = new StreamReader(notificationPath))
            using (var jr = new JsonTextReader(sr))
            {
                data = await JObject.LoadAsync(jr).ConfigureAwait(false);
            }

            data["uid"] = uid;
            Log.LogMessage(MessageImportance.Normal, "Posting notification {0}...", uid);

            await ReplaceVaultAsync(data);

            await GrafanaClient.CreateNotificationChannelAsync(data).ConfigureAwait(false);
        }
    }

    private async Task PostContactPointsAsync()
    {
        // Check if notification directory exists (optional feature)
        if (!Directory.Exists(EnvironmentNotificationDirectory))
        {
            Log.LogMessage(MessageImportance.Low, "No notification directory found at {0}, skipping contact points", EnvironmentNotificationDirectory);
            return;
        }

        foreach (string notificationPath in Directory.GetFiles(EnvironmentNotificationDirectory,
                     "*" + NotificationExtension,
                     SearchOption.AllDirectories))
        {
            JObject data;
            using (var sr = new StreamReader(notificationPath))
            using (var jr = new JsonTextReader(sr))
            {
                data = await JObject.LoadAsync(jr).ConfigureAwait(false);
            }

            string name = data.Value<string>("name");
            Log.LogMessage(MessageImportance.Normal, "Posting contact point {0}...", name);

            await ReplaceVaultAsync(data);

            await GrafanaClient.CreateContactPointAsync(data).ConfigureAwait(false);
        }
    }

    private async Task PostAlertRulesAsync()
    {
        // Check if alert rules directory exists 
        if (!Directory.Exists(AlertRuleDirectory))
        {
            Log.LogMessage(MessageImportance.Low, "No alert rules directory found at {0}, skipping alert rules", AlertRuleDirectory);
            return;
        }

        Log.LogMessage(MessageImportance.High, "Loading parameters from: {0}", Path.GetFullPath(_parameterFile));
        Log.LogMessage(MessageImportance.High, "Parameters file exists: {0}", File.Exists(_parameterFile));

        // Load parameters for deparameterization
        List<Parameter> parameters;
        using (StreamReader sr = new StreamReader(_parameterFile))
        using (JsonReader jr = new JsonTextReader(sr))
        {
            JsonSerializer jsonSerializer = new JsonSerializer();
            parameters = jsonSerializer.Deserialize<List<Parameter>>(jr);
        }

        if (parameters == null || parameters.Count == 0)
        {
            Log.LogError("Failed to load parameters from {0}", _parameterFile);
            return;
        }

        Log.LogMessage(MessageImportance.High, "Loaded {0} parameters from {1}", parameters.Count, _parameterFile);

        // Ensure all folders referenced by alert rules exist before posting rules
        var alertRuleFiles = Directory.GetFiles(AlertRuleDirectory, "*" + AlertRuleExtension, SearchOption.AllDirectories);
        var seenFolderUids = new HashSet<string>(StringComparer.Ordinal);
        var ruleGroupIntervals = new Dictionary<(string FolderUid, string RuleGroup), int>();
        var ruleGroupsWithoutExplicitIntervals = new HashSet<(string FolderUid, string RuleGroup)>();
        foreach (string alertRulePath in alertRuleFiles)
        {
            JObject data;
            using (var sr = new StreamReader(alertRulePath))
            using (var jr = new JsonTextReader(sr))
            {
                data = await JObject.LoadAsync(jr).ConfigureAwait(false);
            }
            data = GrafanaSerialization.DeparameterizeDashboard(data, parameters, _environment);
            string folderUID = data.Value<string>("folderUID");
            if (!string.IsNullOrEmpty(folderUID) && seenFolderUids.Add(folderUID))
            {
                Log.LogMessage(MessageImportance.Normal, "Ensuring alert rule folder '{0}' exists...", folderUID);
                await GrafanaClient.CreateFolderAsync(folderUID, folderUID).ConfigureAwait(false);
            }

            string ruleGroup = data.Value<string>("ruleGroup");
            if (!string.IsNullOrEmpty(folderUID) && !string.IsNullOrEmpty(ruleGroup))
            {
                var key = (folderUID, ruleGroup);
                if (data.TryGetValue("evaluationIntervalSeconds", out JToken intervalToken))
                {
                    int intervalSeconds = intervalToken.Type == JTokenType.Integer
                        ? intervalToken.Value<int>()
                        : 0;
                    if (intervalSeconds <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Alert rule {alertRulePath} must specify a positive evaluationIntervalSeconds.");
                    }

                    if (ruleGroupsWithoutExplicitIntervals.Contains(key))
                    {
                        throw new InvalidOperationException(
                            $"Every managed rule in alert rule group '{folderUID}/{ruleGroup}' must specify evaluationIntervalSeconds.");
                    }

                    if (ruleGroupIntervals.TryGetValue(key, out int existingInterval) &&
                        existingInterval != intervalSeconds)
                    {
                        throw new InvalidOperationException(
                            $"Alert rule group '{folderUID}/{ruleGroup}' has conflicting evaluation intervals: " +
                            $"{existingInterval} and {intervalSeconds} seconds.");
                    }

                    ruleGroupIntervals[key] = intervalSeconds;
                }
                else
                {
                    if (ruleGroupIntervals.ContainsKey(key))
                    {
                        throw new InvalidOperationException(
                            $"Every managed rule in alert rule group '{folderUID}/{ruleGroup}' must specify evaluationIntervalSeconds.");
                    }

                    ruleGroupsWithoutExplicitIntervals.Add(key);
                }
            }
        }

        foreach (string alertRulePath in Directory.GetFiles(AlertRuleDirectory,
                     "*" + AlertRuleExtension,
                     SearchOption.AllDirectories))
        {
            JObject data;
            using (var sr = new StreamReader(alertRulePath))
            using (var jr = new JsonTextReader(sr))
            {
                data = await JObject.LoadAsync(jr).ConfigureAwait(false);
            }

            string uid = data.Value<string>("uid");
            string title = data.Value<string>("title");
            Log.LogMessage(MessageImportance.Normal, "Posting alert rule {0} ({1})...", uid, title);

            // Replace [parameter(...)] placeholders with environment-specific values
            data = GrafanaSerialization.DeparameterizeDashboard(data, parameters, _environment);
            data.Remove("evaluationIntervalSeconds");

            // Log the final JSON for debugging
            Log.LogMessage(MessageImportance.High, "Alert JSON after parameter replacement: {0}", data.ToString(Formatting.Indented));

            await ReplaceVaultAsync(data);

            await GrafanaClient.CreateAlertRuleAsync(data).ConfigureAwait(false);
        }

        foreach (KeyValuePair<(string FolderUid, string RuleGroup), int> interval in ruleGroupIntervals)
        {
            Log.LogMessage(
                MessageImportance.Normal,
                "Setting alert rule group {0}/{1} evaluation interval to {2} seconds...",
                interval.Key.FolderUid,
                interval.Key.RuleGroup,
                interval.Value);
            await GrafanaClient.SetAlertRuleGroupIntervalAsync(
                interval.Key.FolderUid,
                interval.Key.RuleGroup,
                interval.Value).ConfigureAwait(false);
        }
    }

    private async Task<HashSet<string>> PostDashboardsAsync()
    {
        JArray folderArray = await GrafanaClient.ListFoldersAsync().ConfigureAwait(false);
        List<FolderData> folders = folderArray.Select(f => new FolderData(f.Value<string>("uid"), f.Value<string>("title")))
            .ToList();
        var knownUids = new HashSet<string>();

        List<Parameter> parameters;

        using (StreamReader sr = new StreamReader(_parameterFile))
        using (JsonReader jr = new JsonTextReader(sr))
        {
            JsonSerializer jsonSerializer = new JsonSerializer();
            parameters = jsonSerializer.Deserialize<List<Parameter>>(jr);
        }

        foreach (string dashboardPath in GetAllDashboardPaths())
        {
            string folderName = Path.GetFileName(Path.GetDirectoryName(dashboardPath));
            string dashboardFileName = Path.GetFileName(dashboardPath);
            string uid = GetUidFromDashboardFile(dashboardFileName);
            knownUids.Add(uid);

            FolderData folder = folders.FirstOrDefault(f => f.Title == folderName);

            JObject result = await GrafanaClient.CreateFolderAsync(folderName, folderName).ConfigureAwait(false);
            string folderUid = result["uid"].Value<string>();
            int folderId = result["id"].Value<int>();

            if (folder == null)
            {
                folder = new FolderData(folderUid, folderName);
            }

            folder.Id = folderId;

            JObject data;
            using (var sr = new StreamReader(dashboardPath))
            using (var jr = new JsonTextReader(sr))
            {
                data = await JObject.LoadAsync(jr).ConfigureAwait(false);
            }

            GrafanaSerialization.SetDashboardManagementTags(data, uid, SourceTag);
            data["uid"] = uid;

            data = GrafanaSerialization.DeparameterizeDashboard(data, parameters, _environment);

            Log.LogMessage(MessageImportance.Normal, "Posting dashboard {0}...", uid);

            await GrafanaClient.CreateDashboardAsync(data, folderId).ConfigureAwait(false);
        }

        return knownUids;
    }

    private async Task ClearExtraneousDashboardsAsync(HashSet<string> knownUids)
    {
        JArray allTagged = await GrafanaClient.SearchDashboardsByTagAsync(SourceTag).ConfigureAwait(false);
        HashSet<string> toRemove = new HashSet<string>(
            allTagged
                .Where(d => GrafanaSerialization.IsManagedDashboard(d, SourceTag))
                .Select(d => d.Value<string>("uid")));

        // We shouldn't remove the ones we just deployed
        toRemove.ExceptWith(knownUids);

        foreach (string uid in toRemove)
        {
            Log.LogMessage(MessageImportance.Normal, "Deleting extra dashboard {0}...", uid);
            await GrafanaClient.DeleteDashboardAsync(uid).ConfigureAwait(false);
        }
    }

    private async Task DeleteRetiredDashboardsAsync()
    {
        List<Parameter> parameters;
        using (var sr = new StreamReader(_parameterFile))
        using (var jr = new JsonTextReader(sr))
        {
            parameters = new JsonSerializer().Deserialize<List<Parameter>>(jr);
        }

        Parameter retiredDashboardParameter = parameters?
            .FirstOrDefault(parameter => parameter.Name == "retired-dashboard-uids");
        if (retiredDashboardParameter == null ||
            !retiredDashboardParameter.Values.TryGetValue(_environment, out string retiredDashboardValue))
        {
            return;
        }

        var knownUids = new HashSet<string>(
            GetAllDashboardPaths()
                .Select(Path.GetFileName)
                .Select(GetUidFromDashboardFile),
            StringComparer.Ordinal);

        foreach (string uid in GrafanaSerialization.ParseDashboardUidList(retiredDashboardValue))
        {
            if (knownUids.Contains(uid))
            {
                throw new InvalidOperationException(
                    $"Dashboard '{uid}' cannot be both deployed and explicitly retired.");
            }

            Log.LogMessage(MessageImportance.Normal, "Ensuring explicitly retired dashboard {0} is absent...", uid);
            await GrafanaClient.DeleteDashboardAsync(uid).ConfigureAwait(false);
        }
    }

    public async Task<JToken> ReplaceVaultAsync(JToken data)
    {
        switch (data)
        {
            case JObject jObject:
                foreach (var (key, value) in jObject)
                {
                    jObject[key] = await ReplaceVaultAsync(value);
                }
                return jObject;

            case JArray jArray:
                for (int i = 0; i < jArray.Count; i++)
                {
                    jArray[i] = await ReplaceVaultAsync(jArray[i]);
                }
                return jArray;

            case JValue jValue:
            {
                if (jValue.Type != JTokenType.String ||
                    !TryGetSecretName((string)jValue.Value, out string secretName))
                {
                    return jValue;
                }

                return await GetSecretAsync(secretName).ConfigureAwait(false);
            }
            default:
                return data;
        }
    }

    private static bool TryGetSecretName(string data, out string secret)
    {
        var r = new Regex(@"\[[vV]ault\((.*)\)\]");
        Match match = r.Match(data);

        if (!match.Success)
        {
            secret = null;
            return false;
        }

        secret = match.Groups[1].Value;
        return true;
    }

    private async Task<string> GetSecretAsync(string name)
    {
        KeyVaultSecret result = await KeyVault.GetSecretAsync(name).ConfigureAwait(false);
        return result.Value;
    }


    private SecretClient GetKeyVaultClient()
    {
        Uri vaultUri = new($"https://{_keyVaultName}.vault.azure.net/");
        return new SecretClient(vaultUri, _tokenCredential);
    }

    private async Task SetHomeDashboardAsync()
    {
        // Load parameters to get home dashboard UID
        List<Parameter> parameters;
        using (StreamReader sr = new StreamReader(_parameterFile))
        using (JsonReader jr = new JsonTextReader(sr))
        {
            JsonSerializer jsonSerializer = new JsonSerializer();
            parameters = jsonSerializer.Deserialize<List<Parameter>>(jr);
        }

        if (parameters == null)
        {
            Log.LogMessage(MessageImportance.Normal, "No parameters file found, skipping home dashboard configuration");
            return;
        }

        // Find the home-dashboard-uid parameter
        var homeDashboardParam = parameters.FirstOrDefault(p => p.Name == "home-dashboard-uid");
        if (homeDashboardParam == null || !homeDashboardParam.Values.TryGetValue(_environment, out string dashboardUid))
        {
            Log.LogMessage(MessageImportance.Normal, "No home-dashboard-uid parameter found for environment {0}, skipping home dashboard configuration", _environment);
            return;
        }

        if (string.IsNullOrWhiteSpace(dashboardUid))
        {
            dashboardUid = string.Empty;
            Log.LogMessage(MessageImportance.Normal, "Clearing the configured home dashboard");
        }
        else
        {
            Log.LogMessage(MessageImportance.Normal, "Setting home dashboard to: {0}", dashboardUid);
        }

        await GrafanaClient.SetHomeDashboardAsync(dashboardUid).ConfigureAwait(false);
        Log.LogMessage(MessageImportance.Normal, "Successfully updated the home dashboard preference");
    }
}
