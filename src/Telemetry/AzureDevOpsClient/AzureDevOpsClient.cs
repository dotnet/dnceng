// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

#nullable enable

namespace Microsoft.DotNet.Internal.AzureDevOps;

public sealed class AzureDevOpsClient : IAzureDevOpsClient
{
    private readonly ILogger<AzureDevOpsClient> _logger;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _parallelism;

    public AzureDevOpsClient(
        AzureDevOpsClientOptions options,
        ILogger<AzureDevOpsClient> logger,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _logger.LogInformation("Constructing AzureDevOpsClient for org {organization}", options.Organization);
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.BaseAddress = new Uri($"https://dev.azure.com/{options.Organization}/");
        _parallelism = new SemaphoreSlim(options.MaxParallelRequests, options.MaxParallelRequests);
        if (!string.IsNullOrEmpty(options.AccessToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($":{options.AccessToken}"))
            );
        }
    }

    /// <summary>
    ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-5.0
    /// </summary>
    private async Task<JsonResult> ListBuildsRaw(
        string project,
        string? continuationToken,
        DateTimeOffset? minTime,
        int? limit,
        CancellationToken cancellationToken)
    {
        StringBuilder builder = GetProjectApiRootBuilder(project);
        builder.Append("build/builds?");
        builder.Append($"continuationToken={continuationToken}&");
        builder.Append("queryOrder=finishTimeAscending&");
            
        if (minTime.HasValue)
        {
            builder.Append($"minTime={minTime.Value.UtcDateTime:O}&");
        }

        if (limit.HasValue)
        {
            builder.Append($"$top={limit}&");
        }

        builder.Append("statusFilter=completed&");
        builder.Append("api-version=5.0");
        return await GetJsonResult(builder.ToString(), cancellationToken);
    }

    /// <summary>
    ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-5.0
    /// </summary>
    public async Task<Build[]> ListBuilds(
        string project,
        CancellationToken cancellationToken,
        DateTimeOffset? minTime = default,
        int? limit = default)
    {
        var buildList = new List<Build>();
        string? continuationToken = null;
        do
        {
            JsonResult result = await ListBuildsRaw(project, continuationToken, minTime, limit, cancellationToken);
            continuationToken = result.ContinuationToken;
            JObject root = JObject.Parse(result.Body);
            var array = (JArray?) root["value"];
            var builds = array?.ToObject<Build[]>();
            if (builds != null)
            {
                buildList.AddRange(builds);
            }
        } while (continuationToken != null && (!limit.HasValue || buildList.Count < limit.Value));

        return buildList.ToArray();
    }

    public async Task<AzureDevOpsProject[]?> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        JsonResult result = await GetJsonResult($"_apis/projects?api-version=5.1", cancellationToken);
        return JsonConvert.DeserializeObject<AzureDevOpsArrayOf<AzureDevOpsProject>>(result.Body)?.Value;
    }

    public async Task<Build?> GetBuildAsync(string project, long buildId, CancellationToken cancellationToken = default)
    {
        StringBuilder builder = GetProjectApiRootBuilder(project);
        builder.Append($"build/builds/{buildId}?api-version=5.1");
        JsonResult jsonResult = await GetJsonResult(builder.ToString(), cancellationToken);
        return JsonConvert.DeserializeObject<Build>(jsonResult.Body);
    }

    public async Task<(BuildChange[]? changes, int? truncatedChangeCount)?> GetBuildChangesAsync(string project, long buildId, CancellationToken cancellationToken = default)
    {
        StringBuilder builder = GetProjectApiRootBuilder(project);
        builder.Append($"build/builds/{buildId}/changes?$top=10&api-version=5.1");
        JsonResult jsonResult = await GetJsonResult(builder.ToString(), cancellationToken);
        var arrayOf = JsonConvert.DeserializeObject<AzureDevOpsArrayOf<BuildChange>>(jsonResult.Body);
        return (arrayOf?.Value, arrayOf?.Count - arrayOf?.Value.Length);
    }

    private async Task<string> GetTimelineRaw(string project, int buildId, CancellationToken cancellationToken)
    {
        StringBuilder builder = GetProjectApiRootBuilder(project);
        builder.Append($"build/builds/{buildId}/timeline?api-version=5.0");
        return (await GetJsonResult(builder.ToString(), cancellationToken)).Body;
    }

    private async Task<string> GetTimelineRaw(string project, int buildId, string id, CancellationToken cancellationToken)
    {
        StringBuilder builder = GetProjectApiRootBuilder(project);
        builder.Append($"build/builds/{buildId}/timeline/{id}?api-version=5.0");
        return (await GetJsonResult(builder.ToString(), cancellationToken)).Body;
    }

    public async Task<Timeline?> GetTimelineAsync(string project, int buildId, CancellationToken cancellationToken)
    {
        string json = await GetTimelineRaw(project, buildId, cancellationToken);
        return JsonConvert.DeserializeObject<Timeline>(json);
    }

    public async Task<Timeline?> GetTimelineAsync(string project, int buildId, string timelineId, CancellationToken cancellationToken)
    {
        string json = await GetTimelineRaw(project, buildId, timelineId, cancellationToken);
        return JsonConvert.DeserializeObject<Timeline>(json);
    }

    public async Task<WorkItem?> CreateRcaWorkItem(string project, string title, CancellationToken cancellationToken)
    {
        Dictionary<string, string> fields = new Dictionary<string, string>();
        fields.Add("System.Title", title);

        string json = await CreateWorkItem(project, "RCA", fields, cancellationToken);
        return JsonConvert.DeserializeObject<WorkItem>(json);
    }

    public async Task<WorkItem?> CreateAlertWorkItem(string project, string title, string description, string[] tags, CancellationToken cancellationToken)
    {
        Dictionary<string, string> fields = new Dictionary<string, string>();
        fields.Add("System.Title", title);
        fields.Add("System.Description", description);
        
        if (tags != null && tags.Length > 0)
        {
            fields.Add("System.Tags", string.Join("; ", tags));
        }

        string json = await CreateWorkItem(project, "Issue", fields, cancellationToken);
        return JsonConvert.DeserializeObject<WorkItem>(json);
    }

    public async Task<WorkItem?> UpdateWorkItemTags(string project, int workItemId, string[] tagsToAdd, string[] tagsToRemove, CancellationToken cancellationToken)
    {
        StringBuilder builder = GetProjectApiRootBuilder(project);
        builder.Append($"wit/workitems/{workItemId}?api-version=6.0");

        WorkItem? existingWorkItem = await GetWorkItem(project, workItemId, cancellationToken);
        if (existingWorkItem == null)
        {
            return null;
        }

        string existingTags = existingWorkItem.Fields.TryGetValue("System.Tags", out object? tagsObj) 
            ? tagsObj?.ToString() ?? string.Empty 
            : string.Empty;

        HashSet<string> tagSet = new HashSet<string>(
            existingTags.Split(new[] { "; " }, StringSplitOptions.RemoveEmptyEntries)
        );

        if (tagsToRemove != null)
        {
            foreach (string tag in tagsToRemove)
            {
                tagSet.Remove(tag);
            }
        }

        if (tagsToAdd != null)
        {
            foreach (string tag in tagsToAdd)
            {
                tagSet.Add(tag);
            }
        }

        List<JsonPatchDocument> patchDocuments = new List<JsonPatchDocument>();
        JsonPatchDocument tagsUpdate = new JsonPatchDocument()
        {
            From = null,
            Op = "add",
            Path = "/fields/System.Tags",
            Value = string.Join("; ", tagSet)
        };
        patchDocuments.Add(tagsUpdate);

        string body = JsonConvert.SerializeObject(patchDocuments);
        string json = (await PatchJsonResult(builder.ToString(), body, cancellationToken)).Body;
        return JsonConvert.DeserializeObject<WorkItem>(json);
    }

    public async Task<string?> AddWorkItemComment(string project, int workItemId, string comment, CancellationToken cancellationToken)
    {
        StringBuilder builder = GetProjectApiRootBuilder(project);
        builder.Append($"wit/workitems/{workItemId}/comments?api-version=6.0-preview");

        JObject commentBody = new JObject
        {
            ["text"] = comment
        };

        string body = JsonConvert.SerializeObject(commentBody);
        string json = (await PostJsonResult(builder.ToString(), body, cancellationToken)).Body;
        return json;
    }

    public async Task<WorkItem[]?> QueryWorkItemsByTag(string project, string tag, CancellationToken cancellationToken)
    {
        StringBuilder builder = GetProjectApiRootBuilder(project);
        builder.Append("wit/wiql?api-version=6.0");

        string wiql = $@"SELECT [System.Id] FROM WorkItems WHERE [System.Tags] CONTAINS '{tag.Replace("'", "''")}' AND [System.State] <> 'Closed' AND [System.State] <> 'Resolved'";
        
        JObject queryBody = new JObject
        {
            ["query"] = wiql
        };

        string body = JsonConvert.SerializeObject(queryBody);
        string json = (await PostJsonResult(builder.ToString(), body, cancellationToken)).Body;
        
        JObject result = JObject.Parse(json);
        JArray? workItemRefs = (JArray?)result["workItems"];
        
        if (workItemRefs == null || workItemRefs.Count == 0)
        {
            return Array.Empty<WorkItem>();
        }

        List<WorkItem> workItems = new List<WorkItem>();
        foreach (JToken workItemRef in workItemRefs)
        {
            int id = workItemRef["id"]?.Value<int>() ?? 0;
            if (id > 0)
            {
                WorkItem? workItem = await GetWorkItem(project, id, cancellationToken);
                if (workItem != null)
                {
                    workItems.Add(workItem);
                }
            }
        }

        return workItems.ToArray();
    }

    private async Task<WorkItem?> GetWorkItem(string project, int workItemId, CancellationToken cancellationToken)
    {
        StringBuilder builder = GetProjectApiRootBuilder(project);
        builder.Append($"wit/workitems/{workItemId}?api-version=6.0");
        
        JsonResult result = await GetJsonResult(builder.ToString(), cancellationToken);
        return JsonConvert.DeserializeObject<WorkItem>(result.Body);
    }

    /// <summary>
    /// The method reads the logs as a stream, line by line and tries to match the regexes in order, one regex per line. 
    /// If the consecutive regexes match the lines, the last match is returned.
    /// </summary>
    public async Task<string?> MatchLogLineSequence(
        string logUri,
        IReadOnlyList<Regex> regexes,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, logUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        using Stream logStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using StreamReader reader = new StreamReader(logStream);
        string? line;
        Queue<string> lineCache = new(regexes.Count);
        // need to check if a new line will be loaded when the cache is full, it shouldn't because the first condition will tell it to exit the loop
        while ((line = await reader.ReadLineAsync()) != null && lineCache.Count < regexes.Count())
        {
            lineCache.Enqueue(line);
        }

        // Check if we didn't even have enough lines to fill the cache
        if (lineCache.Count < regexes.Count())
        {
            return null;
        }

        string? result;
        do
        {
            result = CheckLineCache(lineCache, regexes);

            if (result != null)
            {
                return result;
            }

            lineCache.Dequeue();
            if (line != null)
            {
                lineCache.Enqueue(line);
            }
        }
        while ((line = await reader.ReadLineAsync()) != null);

        // This will return the value if it finds something in the last cache, or null if not
        return CheckLineCache(lineCache, regexes);
    }

    private string? CheckLineCache(IEnumerable<string> lineCache, IEnumerable<Regex> regexes)
    {
        string? result = null;

        return lineCache.Zip(regexes, (line, regex) => (line, regex))
            .All(pair => TryMatchRegex(pair.line, pair.regex, out result)) ? result : null;
    }

    public async Task<string?> GetProjectNameAsync(string id)
    {
        var projects = await ListProjectsAsync();
        var map = projects?.ToDictionary(p => p.Id, p => p.Name);
        return map?.GetValueOrDefault(id);
    }

    private bool TryMatchRegex(string line, Regex regex, [NotNullWhen(true)] out string? result)
    {
        var match = regex.Match(line);
        if (match.Success)
        {
            result = match.Groups[1].Value;
            return true;
        }

        result = default;
        return false;
    }

    private async Task<string> CreateWorkItem(string project, string type, Dictionary<string, string> fields, CancellationToken cancellationToken)
    {
        StringBuilder builder = GetProjectApiRootBuilder(project);
        builder.Append($"wit/workitems/${type}?api-version=6.0");

        List<JsonPatchDocument> patchDocuments = new List<JsonPatchDocument>();
        foreach(var field in fields)
        {
            JsonPatchDocument patchDocument = new JsonPatchDocument()
            {
                From = null,
                Op = "add",
                Path = $"/fields/{field.Key}",
                Value = field.Value
            };

            patchDocuments.Add(patchDocument);
        }

        JsonPatchDocument areaPath = new JsonPatchDocument()
        {
            From = null,
            Op = "add",
            Path = "/fields/System.AreaPath",
            Value = "internal\\Dotnet-Core-Engineering"
        };

        patchDocuments.Add(areaPath);

        string body = JsonConvert.SerializeObject(patchDocuments);

        return (await PostJsonResult(builder.ToString(), body, cancellationToken)).Body;
    }

    private StringBuilder GetProjectApiRootBuilder(string project)
    {
        var builder = new StringBuilder();
        builder.Append($"{project}/_apis/");
        return builder;
    }

    private async Task<JsonResult> GetJsonResult(string uri, CancellationToken cancellationToken)
    {
        await _parallelism.WaitAsync(cancellationToken);
        try
        {
            int retry = 5;
            while (true)
            {
                try
                {
                    using (HttpResponseMessage response = await _httpClient.GetAsync(uri, cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();
                        string responseBody = await response.Content.ReadAsStringAsync();
                        response.Headers.TryGetValues("x-ms-continuationtoken",
                            out IEnumerable<string>? continuationTokenHeaders);
                        string? continuationToken = continuationTokenHeaders?.FirstOrDefault();
                        var result = new JsonResult(responseBody, continuationToken);

                        return result;
                    }
                }
                catch (OperationCanceledException e) when (e.CancellationToken == cancellationToken)
                {
                    throw;
                }
                catch (Exception) when (retry -- > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
        }
        finally
        {
            _parallelism.Release();
        }
    }

    private async Task<JsonResult> PostJsonResult(string uri, string body, CancellationToken cancellationToken)
    {
        await _parallelism.WaitAsync(cancellationToken);
        try
        {
            int retry = 5;
            while (true)
            {
                try
                {
                    var content = new StringContent(body, Encoding.UTF8, "application/json-patch+json");

                    using (HttpResponseMessage response = await _httpClient.PostAsync(uri, content, cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();
                        string responseBody = await response.Content.ReadAsStringAsync();
                        response.Headers.TryGetValues("x-ms-continuationtoken",
                            out IEnumerable<string>? continuationTokenHeaders);
                        string? continuationToken = continuationTokenHeaders?.FirstOrDefault();
                        var result = new JsonResult(responseBody, continuationToken);

                        return result;
                    }
                }
                catch (OperationCanceledException e) when (e.CancellationToken == cancellationToken)
                {
                    throw;
                }
                catch (Exception) when (retry-- > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
        }
        finally
        {
            _parallelism.Release();
        }
    }

    public async Task<BuildChangeDetail?> GetChangeDetails(string changeUrl, CancellationToken cancellationToken = default)
    {
        var result = await GetJsonResult(changeUrl, cancellationToken);
        return JsonConvert.DeserializeObject<BuildChangeDetail>(result.Body);
    }

    private async Task<JsonResult> PatchJsonResult(string uri, string body, CancellationToken cancellationToken)
    {
        await _parallelism.WaitAsync(cancellationToken);
        try
        {
            int retry = 5;
            while (true)
            {
                try
                {
                    StringContent content = new StringContent(body, Encoding.UTF8, "application/json-patch+json");

                    using (HttpResponseMessage response = await _httpClient.PatchAsync(uri, content, cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();
                        string responseBody = await response.Content.ReadAsStringAsync();
                        response.Headers.TryGetValues("x-ms-continuationtoken",
                            out IEnumerable<string>? continuationTokenHeaders);
                        string? continuationToken = continuationTokenHeaders?.FirstOrDefault();
                        JsonResult result = new JsonResult(responseBody, continuationToken);

                        return result;
                    }
                }
                catch (OperationCanceledException e) when (e.CancellationToken == cancellationToken)
                {
                    throw;
                }
                catch (Exception) when (retry-- > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
        }
        finally
        {
            _parallelism.Release();
        }
    }
}

public class BuildChangeDetail
{
    [JsonProperty("_links")]
    public BuildLinks? Links { get; set; }
}

public class BuildChange
{
    public BuildChange(string id, IdentityRef author, string message, string type, string displayUri, string location)
    {
        Id = id;
        Author = author;
        Message = message;
        Type = type;
        DisplayUri = displayUri;
        Location = location;
    }

    public string Id { get;  }
    public IdentityRef Author { get; }
    public string Message { get; }

    public string Type { get; }
    public string DisplayUri { get; }
    public string Location { get; }
}

public class AzureDevOpsArrayOf<T>
{
    public AzureDevOpsArrayOf(int count, T[] value)
    {
        Count = count;
        Value = value;
    }

    public int Count { get; }
    public T[] Value { get; }
}

public class AzureDevOpsProject
{
    public AzureDevOpsProject(string id, string name, string description, string url, string state, int revision, string visibility)
    {
        Id = id;
        Name = name;
        Description = description;
        Url = url;
        State = state;
        Revision = revision;
        Visibility = visibility;
    }

    public string Id { get; }
    public string Name { get;}
    public string Description { get; }
    public string Url { get; }
    public string State { get; }
    public int Revision { get; }
    public string Visibility { get; }

}

public sealed class JsonResult
{
    public JsonResult(string body, string? continuationToken)
    {
        Body = body;
        ContinuationToken = continuationToken;
    }

    public string Body { get; }
    public string? ContinuationToken { get; }
}
