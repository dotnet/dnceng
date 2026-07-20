// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Azure;
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Azure.Data.Tables;
using DotNet.Status.Web.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StatusWebAnnotationEntity = DotNet.Status.Web.Models.AnnotationEntity;
using Azure.Identity;

namespace DotNet.Status.Web.Controllers;

[ApiController]
[Route("api/deployment")]
public class DeploymentController : ControllerBase
{
    private readonly IHostEnvironment _env;
    private readonly IOptionsMonitor<GrafanaOptions> _grafanaOptions;
    private readonly ILogger<DeploymentController> _logger;

    public DeploymentController(
        IOptionsMonitor<GrafanaOptions> grafanaOptions,
        ILogger<DeploymentController> logger,
        IHostEnvironment env)
    {
        _grafanaOptions = grafanaOptions;
        _logger = logger;
        _env = env;
    }

    [HttpPost("{service}/{id}/start")]
    public async Task<IActionResult> MarkStart([Required] string service, [Required] string id)
    {
        _logger.LogInformation("Recording start of deployment of '{service}' with id '{id}'", service, id);

        // Recording a deployment annotation is best-effort telemetry and must never fail the calling
        // deployment pipeline. The annotation is written to Azure Table storage, which Grafana reads
        // directly; any failure is logged and swallowed so the endpoint returns success instead of a 500.
        try
        {
            TableClient table = await GetCloudTable();
            await table.UpsertEntityAsync(new StatusWebAnnotationEntity(service, id)
            {
                Started = DateTimeOffset.UtcNow,
                ETag = new Azure.ETag("*")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record start of deployment of '{service}' with id '{id}'", service, id);
        }

        return NoContent();
    }

    [HttpPost("{service}/{id}/end")]
    public async Task<IActionResult> MarkEnd([Required] string service, [Required] string id)
    {
        _logger.LogInformation("Recording end of deployment of '{service}' with id '{id}'", service, id);

        // As with MarkStart, recording the end of a deployment is best-effort telemetry written to
        // Azure Table storage and must never fail the calling deployment pipeline. Any failure is
        // logged and swallowed.
        try
        {
            TableClient table = await GetCloudTable();
            _logger.LogInformation("Looking for existing deployment");
            var getResult = await table.GetEntityIfExistsAsync<StatusWebAnnotationEntity>(service, id);
            _logger.LogTrace("Table response code {responseCode}", getResult.GetRawResponse().Status);
            if (!getResult.HasValue)
            {
                _logger.LogWarning("No deployment start record found for '{service}' with id '{id}'; nothing to mark as ended", service, id);
                return NoContent();
            }

            var annotation = getResult.Value;

            _logger.LogTrace("Updating end time of deployment...");
            annotation.Ended = DateTimeOffset.UtcNow;

            var updateResult = await table.UpdateEntityAsync(annotation, annotation.ETag, TableUpdateMode.Replace);
            _logger.LogInformation("Update response code {responseCode}", updateResult.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record end of deployment of '{service}' with id '{id}'", service, id);
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
            table = new TableClient(new Uri(options.TableUri, UriKind.Absolute), options.TableName, new ManagedIdentityCredential());
        }
        return table;
    }
}
