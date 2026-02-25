// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotNet.Status.Web;

public sealed class AzureDevOpsWorkItemClient : IAzureDevOpsWorkItemClient
{
    private const string AzureDevOpsScope = "499b84ac-1321-427f-aa17-267ca6975798/.default";
    private const string ApiVersion = "7.1";

    private readonly HttpClient _httpClient;
    private readonly TokenCredential _tokenCredential;
    private readonly ILogger<AzureDevOpsWorkItemClient> _logger;

    public AzureDevOpsWorkItemClient(
        string organization,
        TokenCredential tokenCredential,
        IHttpClientFactory httpClientFactory,
        ILogger<AzureDevOpsWorkItemClient> logger)
    {
        _tokenCredential = tokenCredential;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri($"https://dev.azure.com/{organization}/");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private async Task<string> GetBearerTokenAsync(CancellationToken cancellationToken)
    {
        TokenRequestContext context = new TokenRequestContext(new[] { AzureDevOpsScope });
        AccessToken token = await _tokenCredential.GetTokenAsync(context, cancellationToken);
        return token.Token;
    }

    private async Task<HttpRequestMessage> BuildRequestAsync(HttpMethod method, string url, CancellationToken cancellationToken)
    {
        string bearerToken = await GetBearerTokenAsync(cancellationToken);
        HttpRequestMessage request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return request;
    }

    public async Task<int> CreateWorkItemAsync(
        string project,
        string workItemType,
        Dictionary<string, object> fields,
        CancellationToken cancellationToken = default)
    {
        string url = $"{project}/_apis/wit/workitems/${Uri.EscapeDataString(workItemType)}?api-version={ApiVersion}";

        List<object> patchOps = fields
            .Select(kvp => (object)new { op = "add", path = $"/fields/{kvp.Key}", value = kvp.Value })
            .ToList();

        string body = JsonConvert.SerializeObject(patchOps);

        HttpRequestMessage request = await BuildRequestAsync(HttpMethod.Post, url, cancellationToken);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json-patch+json");

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        JObject json = JObject.Parse(responseBody);
        int workItemId = json["id"]!.Value<int>();
        _logger.LogInformation("Created Azure DevOps work item {workItemId}", workItemId);
        return workItemId;
    }

    public async Task<Dictionary<string, string>> GetWorkItemFieldsAsync(
        int workItemId,
        CancellationToken cancellationToken = default)
    {
        string url = $"_apis/wit/workitems/{workItemId}?api-version={ApiVersion}";

        HttpRequestMessage request = await BuildRequestAsync(HttpMethod.Get, url, cancellationToken);

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        JObject json = JObject.Parse(responseBody);
        JObject fieldsJson = (JObject)json["fields"]!;
        return fieldsJson.Properties().ToDictionary(p => p.Name, p => p.Value.ToString());
    }

    public async Task UpdateWorkItemFieldsAsync(
        int workItemId,
        Dictionary<string, object> fields,
        CancellationToken cancellationToken = default)
    {
        string url = $"_apis/wit/workitems/{workItemId}?api-version={ApiVersion}";

        List<object> patchOps = fields
            .Select(kvp => (object)new { op = "replace", path = $"/fields/{kvp.Key}", value = kvp.Value })
            .ToList();

        string body = JsonConvert.SerializeObject(patchOps);

        HttpRequestMessage request = await BuildRequestAsync(HttpMethod.Patch, url, cancellationToken);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json-patch+json");

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task AddCommentAsync(
        string project,
        int workItemId,
        string text,
        CancellationToken cancellationToken = default)
    {
        string url = $"{project}/_apis/wit/workItems/{workItemId}/comments?api-version={ApiVersion}-preview.3";

        string body = JsonConvert.SerializeObject(new { text });

        HttpRequestMessage request = await BuildRequestAsync(HttpMethod.Post, url, cancellationToken);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<int[]> QueryWorkItemsByWiqlAsync(
        string project,
        string wiql,
        CancellationToken cancellationToken = default)
    {
        string url = $"{project}/_apis/wit/wiql?api-version={ApiVersion}";

        string body = JsonConvert.SerializeObject(new { query = wiql });

        HttpRequestMessage request = await BuildRequestAsync(HttpMethod.Post, url, cancellationToken);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        JObject json = JObject.Parse(responseBody);
        JArray workItems = (JArray)(json["workItems"] ?? new JArray());
        return workItems.Select(wi => wi["id"]!.Value<int>()).ToArray();
    }
}
