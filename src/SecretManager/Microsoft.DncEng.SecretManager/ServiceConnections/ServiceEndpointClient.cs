using Azure.Core;
using Microsoft.DncEng.CommandLineLib.Authentication;
using Microsoft.DncEng.SecretManager.ServiceConnections.RestModels;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager.ServiceConnections;

#nullable enable

public class ServiceEndpointClient
{
    public class Configuration
    {
        public string Organization { get; set; }
        public string Project { get; set; }

        public Configuration() { }

        public Configuration(string organization, string project)
        {
            Organization = organization;
            Project = project;
        }
    }

    private static readonly string _apiVersion = "7.2-preview.4";

    private readonly HttpClient _httpClient;
    private readonly Configuration config;
    private readonly ITokenCredentialProvider _tokenCredentialProvider;
    private AccessToken? _currentAccessToken;

    private static readonly JsonSerializerOptions _jsonSerializerOptions = new(JsonSerializerDefaults.Web);

    public ServiceEndpointClient(HttpClient httpClient, IOptions<Configuration> config, ITokenCredentialProvider credentialProvider)
    {
        _httpClient = httpClient;
        this.config = config.Value;
        _tokenCredentialProvider = credentialProvider;
    }

    private async Task GetOrUpdateTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_currentAccessToken is null || _currentAccessToken.Value.RefreshOn < DateTimeOffset.UtcNow)
        {
            TokenCredential tokenCredential = await _tokenCredentialProvider.GetCredentialAsync();
            // 499b84ac-1321-427f-aa17-267ca6975798 is the Azure DevOps API application
            // https://ms.portal.azure.com/#view/Microsoft_AAD_IAM/ManagedAppMenuBlade/~/Overview/objectId/71dba5a0-a77c-4b64-bcc4-f5f98be267fe/appId/499b84ac-1321-427f-aa17-267ca6975798
            TokenRequestContext requestContext = new(["499b84ac-1321-427f-aa17-267ca6975798"]);
            AccessToken _currentAccessToken = await tokenCredential.GetTokenAsync(requestContext, cancellationToken);

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(
                Encoding.ASCII.GetBytes($":{_currentAccessToken.Token}")));
        }
    }

    // https://learn.microsoft.com/en-us/rest/api/azure/devops/serviceendpoint/endpoints/get?view=azure-devops-rest-7.2&tabs=HTTP
    public async Task<ServiceEndpoint> Get(Guid id, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("Whoa!");

        Uri endpoint = CreateGetUri(config, id, _apiVersion);

        AdoServiceEndpoint? responseBody;
        try
        {
            await GetOrUpdateTokenAsync(cancellationToken);
            responseBody = await _httpClient.GetFromJsonAsync<AdoServiceEndpoint>(endpoint, _jsonSerializerOptions, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ServiceEndpointClientException("Failed to get service endpoints", ex);
        }

        if (responseBody is null)
            throw new ServiceEndpointClientException();

        ServiceEndpoint remappedEndpoint = ServiceEndpoint.Create(responseBody);

        return remappedEndpoint;
    }

    // https://learn.microsoft.com/en-us/rest/api/azure/devops/serviceendpoint/endpoints/get-service-endpoints?view=azure-devops-rest-7.2&tabs=HTTP
    public async Task<IEnumerable<ServiceEndpoint>> GetAll(CancellationToken cancellationToken = default)
    {
        Uri endpoint = CreateGetAllUri(config, _apiVersion);

        GetServiceEndpointsResponse? responseBody;
        try
        {
            await GetOrUpdateTokenAsync(cancellationToken);
            responseBody = await _httpClient.GetFromJsonAsync<GetServiceEndpointsResponse>(endpoint, _jsonSerializerOptions, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ServiceEndpointClientException("Failed to get service endpoints", ex);
        }

        if (responseBody is null)
            throw new ServiceEndpointClientException();

        IEnumerable<ServiceEndpoint> remappedEndpoints = responseBody.Value.Select(ServiceEndpoint.Create);

        return remappedEndpoints;
    }

    public async Task Update(Guid id, ServiceEndpointUpdateData data, CancellationToken cancellationToken = default)
    {
        // Get all data on the existing endpoint
        Uri getEndpoint = CreateGetUri(config, id, _apiVersion);

        // Keep as a JsonNode so we can make the needed modifications without needing to understand the full schema
        await GetOrUpdateTokenAsync(cancellationToken);
        JsonNode? getResponseBody = await _httpClient.GetFromJsonAsync<JsonNode>(getEndpoint, _jsonSerializerOptions, cancellationToken);

        if (getResponseBody is null)
            throw new ServiceEndpointClientException("GET response body was unrecognized and could not be deserialized");

        // Modify the existing configuration as requested
        if (data.Name is not null)
            getResponseBody["name"] = data.Name;

        if (data.Description is not null)
        {
            getResponseBody["description"] = data.Description;

            JsonArray? serviceEndpointProjectReferences = getResponseBody["serviceEndpointProjectReferences"]?.AsArray();

            if (serviceEndpointProjectReferences is not null)
            {
                foreach (JsonNode? item in serviceEndpointProjectReferences)
                {
                    if (item is not null)
                    {
                        item["description"] = data.Description;
                    }
                }
            }
            else
            {
                throw new ServiceEndpointClientException("'serviceEndpointProjectReferences' is not a valid JsonArray.");
            }

        }

        // TODO: Should I validate the JsonNode schema? If so, do it everywhere.
        if (data.AccessToken is not null)
        {
            if (getResponseBody["authorization"]!["parameters"]!.AsObject().ContainsKey("nugetkey"))
            {
                getResponseBody["authorization"]!["parameters"]!["nugetkey"] = data.AccessToken;
            }
            else if (getResponseBody["authorization"]!["parameters"]!.AsObject().ContainsKey("apitoken"))
            {
                getResponseBody["authorization"]!["parameters"]!["apitoken"] = data.AccessToken;
            }
            else
            {
                throw new ServiceEndpointClientException("Neither 'nugetkey' nor 'apikey' found in the authorization parameters.");
            }
        }

        Uri updateEndpoint = CreateUpdateUri(config, id, _apiVersion);

        //using HttpResponseMessage postResponseBody = await _httpClient.PutAsJsonAsync(updateEndpoint, getResponseBody, cancellationToken);
        using HttpResponseMessage postResponseBody = await HttpClientJsonExtensions.PutAsJsonAsync(_httpClient, updateEndpoint, getResponseBody, cancellationToken);

        try
        {
            postResponseBody.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            throw new ServiceEndpointClientException("Failed to update service endpoint", ex);
        }

        if (postResponseBody is null)
            throw new ServiceEndpointClientException("Failed to parse response body");
    }

    private static Uri CreateGetAllUri(Configuration config, string apiVersion) =>
        new($"https://dev.azure.com/{config.Organization}/{config.Project}/_apis/serviceendpoint/endpoints?api-version={apiVersion}");

    private static Uri CreateGetUri(Configuration config, Guid endpointId, string apiVersion) =>
        new($"https://dev.azure.com/{config.Organization}/{config.Project}/_apis/serviceendpoint/endpoints/{endpointId}?api-version={apiVersion}");

    private static Uri CreateUpdateUri(Configuration config, Guid endpointId, string apiVersion) => CreateGetUri(config, endpointId, apiVersion);
}
