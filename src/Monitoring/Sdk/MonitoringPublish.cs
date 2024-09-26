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

    public string ManagedIdentityId { get; set; }

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
         ChainedTokenCredential tokenCredential = TokenCredentialHelper.GetChainedTokenCredential(ManagedIdentityId);
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
                Log.LogErrorFromException(e, showStackTrace: false, showDetail: false, file: "MonitoringPublish");
                return false;
            }
        }

        return true;
    }
}
