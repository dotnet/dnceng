// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DncEng.GrafanaWatchdog.Configuration;
using Microsoft.DncEng.GrafanaWatchdog.Probing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

IHost host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddOptions<GrafanaWatchdogOptions>()
            .Bind(context.Configuration.GetSection(GrafanaWatchdogOptions.SectionName))
            .Validate(
                options => options.Workspaces.Count > 0,
                "At least one Grafana workspace must be configured.")
            .Validate(
                options => options.Workspaces.All(workspace =>
                    !string.IsNullOrWhiteSpace(workspace.Name)
                    && Uri.TryCreate(workspace.Endpoint, UriKind.Absolute, out Uri? endpoint)
                    && endpoint is not null
                    && endpoint.Scheme == Uri.UriSchemeHttps),
                "Every Grafana workspace must have a name and an absolute HTTPS endpoint.")
            .Validate(
                options => options.Workspaces.Select(workspace => workspace.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                    == options.Workspaces.Count,
                "Grafana workspace names must be unique.")
            .Validate(
                options => options.RetryCount >= 0 && options.RetryCount <= 3,
                "RetryCount must be between 0 and 3.")
            .Validate(
                options => options.RequestTimeout > TimeSpan.Zero && options.RequestTimeout <= TimeSpan.FromMinutes(1),
                "RequestTimeout must be greater than zero and no more than one minute.")
            .ValidateOnStart();

        // DefaultAzureCredential resolves to the Function's system-assigned managed identity when
        // running in Azure, and to the developer's Azure CLI/VS credential when running locally.
        services.AddSingleton<TokenCredential>(new DefaultAzureCredential());
        services.AddHttpClient<IGrafanaWatchdogProbe, GrafanaWatchdogProbe>();

        services.AddApplicationInsightsTelemetryWorkerService(options =>
        {
            // These low-volume records drive alerts and must never be sampled out.
            options.EnableAdaptiveSampling = false;
        });
        services.ConfigureFunctionsApplicationInsights();
    })
    .ConfigureLogging(logging =>
    {
        // Works around https://github.com/Azure/azure-functions-dotnet-worker/issues/1182: without
        // this, the Application Insights logger provider is registered a second time and duplicates
        // every log entry shipped to Application Insights.
        logging.Services.Configure<LoggerFilterOptions>(options =>
        {
            LoggerFilterRule? defaultRule = options.Rules.FirstOrDefault(rule =>
                rule.ProviderName == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");
            if (defaultRule is not null)
            {
                options.Rules.Remove(defaultRule);
            }
        });
    })
    .Build();

await host.RunAsync().ConfigureAwait(false);
