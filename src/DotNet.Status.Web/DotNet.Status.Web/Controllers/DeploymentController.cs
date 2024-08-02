// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Azure;
using System;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;
using DotNet.Status.Web.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using StatusWebAnnotationEntity = DotNet.Status.Web.Models.AnnotationEntity;
using Microsoft.WindowsAzure.Storage.Table;
using Azure.Identity;

namespace DotNet.Status.Web.Controllers;

[ApiController]
[Route("api/deployment")]
public class DeploymentController : ControllerBase
{
    private readonly IHostEnvironment _env;
    private readonly ExponentialRetry _retry;
    private readonly IOptionsMonitor<GrafanaOptions> _grafanaOptions;
    private readonly ILogger<DeploymentController> _logger;

    public DeploymentController(
        ExponentialRetry retry,
        IOptionsMonitor<GrafanaOptions> grafanaOptions,
        ILogger<DeploymentController> logger,
        IHostEnvironment env)
    {
        _retry = retry;
        _grafanaOptions = grafanaOptions;
        _logger = logger;
        _env = env;
    }

    [HttpPost("{service}/{id}/start")]
    public async Task<IActionResult> MarkStart([Required] string service, [Required] string id)
    {
        _logger.LogInformation("Recording start of deployment of '{service}' with id '{id}'", service, id);
        NewGrafanaAnnotationRequest content = new NewGrafanaAnnotationRequest
        {
            Text = $"Deployment of {service}",
            Tags = new[] {"deploy", $"deploy-{service}", service},
            Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
            
        NewGrafanaAnnotationResponse annotation;
        using (var client = new HttpClient(new HttpClientHandler() { CheckCertificateRevocationList = true }))
        {
            annotation = await _retry.RetryAsync(async () =>
                {
                    GrafanaOptions grafanaOptions = _grafanaOptions.CurrentValue;
                    _logger.LogInformation("Creating annotation to {url}", grafanaOptions.BaseUrl);
                    using (var request = new HttpRequestMessage(HttpMethod.Post,
                               $"{grafanaOptions.BaseUrl}/api/annotations"))
                    {
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        request.Headers.Authorization =
                            new AuthenticationHeaderValue("Bearer", grafanaOptions.ApiToken);
                        request.Content = CreateObjectContent(content);

                        using (HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None))
                        {
                            _logger.LogTrace("Response from grafana {responseCode} {reason}", response.StatusCode, response.ReasonPhrase);
                            response.EnsureSuccessStatusCode();
                            return await ReadObjectContent<NewGrafanaAnnotationResponse>(response.Content);
                        }
                    }
                },
                e => _logger.LogWarning(e, "Failed to send new annotation"),
                e => true
            );
        }
        _logger.LogInformation("Created annotation {annotationId}, inserting into table", annotation.Id);

        TableClient table = await GetCloudTable();
        await table.UpsertEntityAsync(new DotNet.Status.Web.Models.AnnotationEntity(service, id, annotation.Id)
        {
            ETag = new Azure.ETag("*")
        });
        return NoContent();
    }

    [HttpPost("{service}/{id}/end")]
    public async Task<IActionResult> MarkEnd([Required] string service, [Required] string id)
    {
        _logger.LogInformation("Recording end of deployment of '{service}' with id '{id}'", service, id);
        TableClient table = await GetCloudTable();
        _logger.LogInformation("Looking for existing deployment");
        var getResult = await table.GetEntityIfExistsAsync<StatusWebAnnotationEntity>(service, id);
        _logger.LogTrace("Table response code {responseCode}", getResult.GetRawResponse().Status);
        if (!getResult.HasValue)
        {
            return NotFound();
        }

        var annotation = getResult.Value;

        _logger.LogTrace("Updating end time of deployment...");
        annotation.Ended = DateTimeOffset.UtcNow;
       
        var updateResult = await table.UpdateEntityAsync(annotation, annotation.ETag, TableUpdateMode.Replace);
        _logger.LogInformation("Update response code {responseCode}", updateResult.Status);
            
        var content = new NewGrafanaAnnotationRequest
        {
            TimeEnd = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        using (var client = new HttpClient(new HttpClientHandler() { CheckCertificateRevocationList = true }))
        {
            await _retry.RetryAsync(async () =>
                {
                    GrafanaOptions grafanaOptions = _grafanaOptions.CurrentValue;
                    _logger.LogInformation("Updating annotation {annotationId} to {url}", annotation.GrafanaAnnotationId, grafanaOptions.BaseUrl);
                    using (var request = new HttpRequestMessage(HttpMethod.Patch,
                               $"{grafanaOptions.BaseUrl}/api/annotations/{annotation.GrafanaAnnotationId}"))
                    {
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        request.Headers.Authorization =
                            new AuthenticationHeaderValue("Bearer", grafanaOptions.ApiToken);
                        request.Content = CreateObjectContent(content);
                        using (HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None))
                        {
                            _logger.LogTrace("Response from grafana {responseCode} {reason}", response.StatusCode, response.ReasonPhrase);
                            response.EnsureSuccessStatusCode();
                        }
                    }
                },
                e => _logger.LogWarning(e, "Failed to send new annotation"),
                e => true
            );
        }

        return NoContent();
    }

    private async Task<TableClient> GetCloudTable()
    {
        TableClient table;
        GrafanaOptions options = _grafanaOptions.CurrentValue;
        if (_env.IsDevelopment())
        {
            table = new TableClient("UseDevelopmentStorage=true", options.TableName);
            await table.CreateIfNotExistsAsync();
        }
        else
        {
            table = new TableClient(new Uri(options.TableUri, UriKind.Absolute), options.TableName, new DefaultAzureCredential());
        }
        return table;
    }

    private static StringContent CreateObjectContent(object content)
    {
        StringWriter writer = new StringWriter();
        s_grafanaSerializer.Serialize(writer, content);
        return new StringContent(writer.ToString(), Encoding.UTF8, "application/json");
    }

    private static async Task<T> ReadObjectContent<T>(HttpContent content)
    {
        using (var streamReader = new StreamReader(await content.ReadAsStreamAsync()))
        using (var jsonReader = new JsonTextReader(streamReader))
        {
            return s_grafanaSerializer.Deserialize<T>(jsonReader);
        }
    }

    private static readonly JsonSerializer s_grafanaSerializer = new JsonSerializer
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Formatting = Formatting.None,
    };
}

public class DeploymentStartRequest
{
    [Required]
    public string Service { get; set; }
}

public class NewGrafanaAnnotationRequest
{
    public int? DashboardId { get; set; }
    public int? PanelId { get; set; }
    public long? Time { get; set; }
    public long? TimeEnd { get; set; }
    public string[] Tags { get; set; }
    public string Text { get; set; }
}

public class NewGrafanaAnnotationResponse
{
    public string Message { get; set; }
    public int Id { get; set; }
}
