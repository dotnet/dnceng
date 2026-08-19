// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
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
    private static readonly Regex VaultReferencePattern = new(
        @"\A\[(?i:vault)\((?:(?<vault>[A-Za-z0-9-]+)/)?(?<secret>[A-Za-z0-9-]+)\)\]\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _keyVaultName;
    private readonly TokenCredential _tokenCredential;
    private readonly IReadOnlyDictionary<string, TokenCredential> _namedVaultCredentials;
    private readonly ConcurrentDictionary<string, SecretClient> _keyVaultClients =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly string _environment;
    private readonly string _parameterFile;

    public DeployPublisher(
        GrafanaClient grafanaClient,
        string keyVaultName,
        TokenCredential tokenCredential,
        string sourceTagValue,
        string dashboardDirectory,
        string datasourceDirectory,
        string notificationDirectory,
        string environment,
        string parametersFile,
        TaskLoggingHelper log,
        IReadOnlyDictionary<string, TokenCredential> namedVaultCredentials = null) : base(
        grafanaClient, sourceTagValue, dashboardDirectory, datasourceDirectory, notificationDirectory, log)
    {
        _keyVaultName = keyVaultName;
        _tokenCredential = tokenCredential;
        _namedVaultCredentials = namedVaultCredentials ??
            new Dictionary<string, TokenCredential>(StringComparer.OrdinalIgnoreCase);
        _environment = environment;
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

        await PostDashboardsAsync().ConfigureAwait(false);

        await SetHomeDashboardAsync().ConfigureAwait(false);
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

            // Log the final JSON for debugging
            Log.LogMessage(MessageImportance.High, "Alert JSON after parameter replacement: {0}", data.ToString(Formatting.Indented));

            await ReplaceVaultAsync(data);

            await GrafanaClient.CreateAlertRuleAsync(data).ConfigureAwait(false);
        }
    }

    private async Task PostDashboardsAsync()
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

            JArray tagArray = null;
            if (data.TryGetValue("tags", out JToken tagToken))
            {
                tagArray = tagToken as JArray;
            }

            if (tagArray == null)
            {
                tagArray = new JArray();
            }

            var newTags = new JArray();
            foreach (JToken tag in tagArray)
            {
                if (tag.Value<string>().StartsWith(BaseUidTagPrefix) ||
                    tag.Value<string>().StartsWith(SourceTagPrefix))
                {
                    continue;
                }

                newTags.Add(tag);
            }

            tagArray.Add(GetUidTag(uid));
            tagArray.Add(SourceTag);
            data["tags"] = newTags;
            data["uid"] = uid;

            data = GrafanaSerialization.DeparameterizeDashboard(data, parameters, _environment);

            Log.LogMessage(MessageImportance.Normal, "Posting dashboard {0}...", uid);

            await GrafanaClient.CreateDashboardAsync(data, folderId).ConfigureAwait(false);
        }

        await ClearExtraneousDashboardsAsync(knownUids);
    }

    private async Task ClearExtraneousDashboardsAsync(HashSet<string> knownUids)
    {
        JArray allTagged = await GrafanaClient.SearchDashboardsByTagAsync(SourceTag).ConfigureAwait(false);
        HashSet<string> toRemove =  new HashSet<string>(allTagged.Where(IsManagedDashboard).Select(d => d.Value<string>("uid")));

        // We shouldn't remove the ones we just deployed
        toRemove.ExceptWith(knownUids);

        foreach (string uid in toRemove)
        {
            Log.LogMessage(MessageImportance.Normal, "Deleting extra dashboard {0}...", uid);
            await GrafanaClient.DeleteDashboardAsync(uid).ConfigureAwait(false);
        }
    }

    private static bool IsManagedDashboard(JToken d)
    {
        string uid = d.Value<string>("uid");
        // If the uid tag (which we set whenever we publish) doesn't match, that means someone copied it
        // so it's not managed by us. If it does match, that means it is managed and we deployed it
        return uid == d.Value<JObject>()?.Value<string>(GetUidTag(uid));
    }

    public async Task<JToken> ReplaceVaultAsync(JToken data)
    {
        return await ReplaceVaultAsync(data, _keyVaultName, GetSecretAsync).ConfigureAwait(false);
    }

    internal static async Task<JToken> ReplaceVaultAsync(
        JToken data,
        string defaultVaultName,
        Func<string, string, Task<string>> getSecretAsync)
    {
        switch (data)
        {
            case JObject jObject:
                foreach (var (key, value) in jObject)
                {
                    jObject[key] = await ReplaceVaultAsync(value, defaultVaultName, getSecretAsync).ConfigureAwait(false);
                }
                return jObject;

            case JArray jArray:
                for (int i = 0; i < jArray.Count; i++)
                {
                    jArray[i] = await ReplaceVaultAsync(jArray[i], defaultVaultName, getSecretAsync).ConfigureAwait(false);
                }
                return jArray;

            case JValue jValue:
            {
                if (jValue.Type != JTokenType.String ||
                    !TryGetSecretReference(
                        (string)jValue.Value,
                        defaultVaultName,
                        out string vaultName,
                        out string secretName))
                {
                    return jValue;
                }

                return await getSecretAsync(vaultName, secretName).ConfigureAwait(false);
            }
            default:
                return data;
        }
    }

    internal static bool TryGetSecretReference(
        string data,
        string defaultVaultName,
        out string vaultName,
        out string secretName)
    {
        Match match = VaultReferencePattern.Match(data ?? string.Empty);

        if (!match.Success)
        {
            vaultName = null;
            secretName = null;
            return false;
        }

        vaultName = match.Groups["vault"].Success
            ? match.Groups["vault"].Value
            : defaultVaultName;
        secretName = match.Groups["secret"].Value;
        return true;
    }

    private async Task<string> GetSecretAsync(string vaultName, string secretName)
    {
        SecretClient keyVault = _keyVaultClients.GetOrAdd(vaultName, GetKeyVaultClient);
        KeyVaultSecret result = await keyVault.GetSecretAsync(secretName).ConfigureAwait(false);
        return result.Value;
    }

    private SecretClient GetKeyVaultClient(string vaultName)
    {
        Uri vaultUri = new($"https://{vaultName}.vault.azure.net/");
        TokenCredential credential;
        if (string.Equals(vaultName, _keyVaultName, StringComparison.OrdinalIgnoreCase))
        {
            credential = _tokenCredential;
        }
        else if (!_namedVaultCredentials.TryGetValue(vaultName, out credential))
        {
            throw new InvalidOperationException(
                $"No credential is configured for external Key Vault '{vaultName}'.");
        }

        return new SecretClient(vaultUri, credential);
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
