using Azure.Data.Tables;
using Azure.Identity;
using DotNet.Status.Web.Models;
using DotNet.Status.Web.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DotNet.Status.Web.Controllers;

/// <summary>
/// This Annotations controller serves queries in a format compatible with the Grafana
/// "Simple JSON Datasource". It is used to expose the "Deployments" table data to 
/// Grafana, which may then render the information in dashboards.
/// </summary>
[Route("api/annotations")]
[ApiController]
public class AnnotationsController : ControllerBase
{
    private const int _maximumServerCount = 10; // Safety limit on query complexity
    private readonly ILogger<AnnotationsController> _logger;
    private readonly IOptionsMonitor<GrafanaOptions> _options;
    private readonly IHostEnvironment _env;

    public AnnotationsController(
        ILogger<AnnotationsController> logger,
        IOptionsMonitor<GrafanaOptions> options,
        IHostEnvironment env)
    {
        _logger = logger;
        _options = options;
        _env = env;
    }

    private async Task<TableClient> GetCloudTable()
    {
        TableClient table;
        var options = _options.CurrentValue;
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

    [HttpGet]
    public ActionResult Get()
    {
        return NoContent();
    }

    [HttpPost]
    [Route("query")]
    public ActionResult Query()
    {   // Required endpoint, but not used
        return NoContent();
    }

    [HttpPost]
    [Route("search")]
    public ActionResult Search()
    {   // Required endpoint, but not used
        return NoContent();
    }

    [HttpPost]
    [Route("annotations")]
    public async Task<ActionResult<IEnumerable<AnnotationEntry>>> Post(AnnotationQueryBody annotationQuery, CancellationToken cancellationToken)
    {
        IEnumerable<string> services = (annotationQuery.Annotation.Query?.Split(',') ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim());

        if (services.Count() > _maximumServerCount)
        {
            return BadRequest();
        }

        StringBuilder filterBuilder = new StringBuilder();
        filterBuilder.Append($"Started gt datetime'{annotationQuery.Range.From:O}' and Ended lt datetime'{annotationQuery.Range.To:O}'");
        if (services.Any())
        {
            filterBuilder.Append(" and (");
            filterBuilder.Append(string.Join(" or ", services.Select(s => $"PartitionKey eq '{s}'")));
            filterBuilder.Append(')');
        }

        string filter = filterBuilder.ToString();
        _logger.LogTrace("Compiled filter query: {Query}", filter);

        TableClient tableClient = await GetCloudTable();
        IAsyncEnumerable<DeploymentEntity> entityQuery = tableClient.QueryAsync<DeploymentEntity>(
            filter: filter,
            cancellationToken: cancellationToken);

        List<AnnotationEntry> annotationEntries = new List<AnnotationEntry>();
        await foreach (DeploymentEntity entity in entityQuery)
        {
            AnnotationEntry entry;

            if (entity.Started != null && entity.Ended != null)
            {
                entry = new AnnotationEntry(
                    annotationQuery.Annotation,
                    entity.Started.Value.ToUnixTimeMilliseconds(),
                    $"Deployment of {entity.Service}")
                {
                    IsRange = true,
                    TimeEnd = entity.Ended.Value.ToUnixTimeMilliseconds()
                };
            }
            else if (entity.Started == null && entity.Ended == null)
            {
                continue;
            }
            else
            {
                entry = new AnnotationEntry(
                    annotationQuery.Annotation,
                    entity.Started?.ToUnixTimeMilliseconds() ?? entity.Ended.Value.ToUnixTimeMilliseconds(),
                    $"Deployment of {entity.Service}");
            }

            entry.Tags = new[] { "deploy", $"deploy-{entity.Service}", entity.Service };

            annotationEntries.Add(entry);
        }

        return annotationEntries;
    }

    [HttpPost]
    [HttpGet]
    [Route("grafana")]
    public async Task<ActionResult<IEnumerable<GrafanaAnnotation>>> GetGrafanaAnnotations(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] GrafanaAnnotationQuery query,
        [FromQuery] string from,
        [FromQuery] string to,
        CancellationToken cancellationToken)
    {
        DateTime fromDate, toDate;
        
        if (query?.Range != null)
        {
            // POST request with body
            fromDate = query.Range.From;
            toDate = query.Range.To;
        }
        else if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
        {
            // GET request with query parameters
            if (!DateTime.TryParse(from, out fromDate) || !DateTime.TryParse(to, out toDate))
            {
                return BadRequest("Invalid date format");
            }
        }
        else
        {
            return BadRequest("Missing date range");
        }

        IEnumerable<string> services = (query?.Annotation?.Query?.Split(',') ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim());

        if (services.Count() > _maximumServerCount)
        {
            return new List<GrafanaAnnotation>();
        }

        StringBuilder filterBuilder = new StringBuilder();
        filterBuilder.Append($"Started gt datetime'{fromDate:O}' and Ended lt datetime'{toDate:O}'");
        if (services.Any())
        {
            filterBuilder.Append(" and (");
            filterBuilder.Append(string.Join(" or ", services.Select(s => $"PartitionKey eq '{s}'")));
            filterBuilder.Append(')');
        }

        string filter = filterBuilder.ToString();
        _logger.LogTrace("Compiled Grafana annotation filter query: {Query}", filter);

        TableClient tableClient = await GetCloudTable();
        IAsyncEnumerable<DeploymentEntity> entityQuery = tableClient.QueryAsync<DeploymentEntity>(
            filter: filter,
            cancellationToken: cancellationToken);

        List<GrafanaAnnotation> annotations = new List<GrafanaAnnotation>();
        await foreach (DeploymentEntity entity in entityQuery)
        {
            if (entity.Started == null && entity.Ended == null)
            {
                continue;
            }

            var annotation = new GrafanaAnnotation
            {
                Time = entity.Started?.ToUnixTimeMilliseconds() ?? entity.Ended.Value.ToUnixTimeMilliseconds(),
                Title = $"Deployment of {entity.Service}",
                Tags = new[] { "deployment", "deploy", $"deploy-{entity.Service}", entity.Service },
                Text = $"Service: {entity.Service}"
            };

            if (entity.Started != null && entity.Ended != null)
            {
                annotation.TimeEnd = entity.Ended.Value.ToUnixTimeMilliseconds();
            }

            annotations.Add(annotation);
        }

        return annotations;
    }
}
