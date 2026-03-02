// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.Build.Framework;
using BuildTask = Microsoft.Build.Utilities.Task;

namespace Microsoft.DotNet.Monitoring.Sdk;

public class MonitoringPublish : BuildTask
{
    [Required]
    public string Host { get; set; }

    [Required]
    public string AccessToken { get; set; }

    [Required]
    public string DashboardDirectory { get; set; }

    [Required]
    public string DataSourceDirectory{ get; set; }

    [Required]
    public string NotificationDirectory { get; set; }
        
    [Required]
    public string KeyVaultName { get; set; }

    // For azure pipeline service connection authentication
    public string ClientId { get; set; }
    public string ServiceConnectionId { get; set; }
    public string SystemAccessToken { get; set; }

    //  For client secret authentication
    public string KeyVaultServicePrincipalId { get; set; }
    public string KeyVaultServicePrincipalSecret { get; set; }

    [Required]
    public string Tag { get; set; }

    [Required]
    public string Environment { get; set; }

    [Required]
    public string ParametersFile { get; set; }

    public sealed override bool Execute()
    {
        var s = Assembly.GetExecutingAssembly();
        return ExecuteAsync().GetAwaiter().GetResult();
    }

    private async Task<bool> ExecuteAsync()
    {
        string msftTenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47";
        TokenCredential tokenCredential;

        if (ClientId == null && ServiceConnectionId == null && SystemAccessToken == null && KeyVaultServicePrincipalId == null && KeyVaultServicePrincipalSecret == null)
        {
            tokenCredential = new DefaultAzureCredential(
                new DefaultAzureCredentialOptions()
                {
                    TenantId = msftTenantId
                }
            );
        }
        else if (ClientId != null || ServiceConnectionId != null || SystemAccessToken != null)
        {
            if (ClientId == null || ServiceConnectionId == null || SystemAccessToken == null)
            {
                Log.LogError("Invalid login combination. Set ClientId, ServiceConnectionId and SystemAccessToken for CI, or none for local user authentication.");
                return false;
            }
            else
            {
                tokenCredential = new AzurePipelinesCredential(msftTenantId, ClientId, ServiceConnectionId, SystemAccessToken);
            }
        }
        else
        {
            if (KeyVaultServicePrincipalId == null || KeyVaultServicePrincipalSecret == null)
            {
                Log.LogError("Invalid login combination. Set KeyVaultServicePrincipalId and KeyVaultServicePrincipalSecret for Client Secret authentication.");
                return false;
            }
            else
            {
                tokenCredential = new ClientSecretCredential(msftTenantId, KeyVaultServicePrincipalId, KeyVaultServicePrincipalSecret);
            }
        }

        using (var client = new GrafanaClient(Host, AccessToken))
        using (var deploy = new DeployPublisher(
                   grafanaClient: client,
                   keyVaultName: KeyVaultName,
                   tokenCredential: tokenCredential,
                   sourceTagValue: Tag,
                   dashboardDirectory: DashboardDirectory,
                   datasourceDirectory: DataSourceDirectory,
                   notificationDirectory: NotificationDirectory,
                   environment: Environment,
                   parametersFile: ParametersFile,
                   log: Log))
        {
            try
            {
                await deploy.PostToGrafanaAsync();
            }
            catch (HttpRequestException e)
            {
                Log.LogErrorFromException(e, showStackTrace: true, showDetail: true, file: "MonitoringPublish");
                return false;
            }
            catch (System.Exception e)
            {
                Log.LogErrorFromException(e, showStackTrace: true, showDetail: true, file: "MonitoringPublish");
                return false;
            }
        }

        return true;
    }
}
