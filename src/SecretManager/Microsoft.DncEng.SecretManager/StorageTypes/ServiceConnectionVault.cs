using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.SecretManager.ServiceConnections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager.StorageTypes;

public class ServiceConnectionVaultParameters
{
    public string Organization { get; set; }
    public string Project { get; set; }
}

#nullable enable

[Name("azure-devops-project")]
/// <summary>
/// This represents a collection of Azure DevOps Service Connections, also called Service Endpoints, that are managed by secret-manager. 
/// 
/// Service Connection are grouped in an Azure DevOps project. Not all service connections in a project need be managed by secret-manager; unrecognized service connections are ignored. 
/// 
/// Service Connections are recognized by secret-manager by well-known text in their description.
/// </summary>
public class ServiceConnectionVault : StorageLocationType<ServiceConnectionVaultParameters>
{
    private readonly ILogger<ServiceConnectionVault> _logger;
    private readonly IServiceProvider _serviceProvider;

    public ServiceConnectionVault(ILogger<ServiceConnectionVault> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public override Task EnsureKeyAsync(ServiceConnectionVaultParameters parameters, string name, SecretManifest.Key config) => throw new NotImplementedException();

    /// <summary>
    /// Always returns null as it is not possible to read secret values from service connections. 
    /// </summary>
    public override Task<SecretValue?> GetSecretValueAsync(ServiceConnectionVaultParameters parameters, string name) => Task.FromResult<SecretValue?>(null);

    public override async Task<List<SecretProperties>> ListSecretsAsync(ServiceConnectionVaultParameters parameters)
    {
        IOptions< ServiceEndpointClient.Configuration> serviceEndpointClientOptions = Options.Create<ServiceEndpointClient.Configuration>(new(parameters.Organization, parameters.Project));
        ServiceEndpointClient serviceEndpointClient = ActivatorUtilities.CreateInstance<ServiceEndpointClient>(_serviceProvider, serviceEndpointClientOptions);

        IEnumerable<ServiceEndpoint> endpoints = await serviceEndpointClient.GetAll();

        List<SecretProperties> allSecrets = new();
        int totalEndpointCount = 0;

        foreach (ServiceEndpoint endpoint in endpoints)
        {
            totalEndpointCount++;

            (DateOnly ExpirationDate, DateOnly NextRotationDate)? parseDescriptionResult = ServiceConnectionMagicString.ParseMagicString(endpoint.Description);

            if (parseDescriptionResult is not null)
            {
                _logger.LogDebug("Identified endpoint {EndpointId} with name \"{EndpointName}\" as managed by secret-manager", endpoint.Id, endpoint.Name);

                Dictionary<string, string> tags = new() 
                {
                    { AzureKeyVault.NextRotationOnTag, parseDescriptionResult.Value.NextRotationDate.ToString() }
                };

                SecretProperties secretProperties = new(
                    endpoint.Name,
                    new DateTimeOffset(parseDescriptionResult.Value.ExpirationDate.ToDateTime(TimeOnly.MinValue)),
                    tags.ToImmutableDictionary());

                allSecrets.Add(secretProperties);
            }
            else
            {
                _logger.LogInformation("Did not identify endpoint {EndpointId} with name \"{EndpointName}\" as managed by secret-manager", endpoint.Id, endpoint.Name);
            }
        }

        _logger.LogInformation("Identified {ManagedEndpointCount} out of {TotalEndpointCount} endpoints as managed by secret-manager", allSecrets.Count, totalEndpointCount);

        return allSecrets;
    }

    public override async Task SetSecretValueAsync(ServiceConnectionVaultParameters parameters, string name, SecretValue value)
    {
        IOptions<ServiceEndpointClient.Configuration> serviceEndpointClientOptions = Options.Create<ServiceEndpointClient.Configuration>(new(parameters.Organization, parameters.Project));
        ServiceEndpointClient serviceEndpointClient = ActivatorUtilities.CreateInstance<ServiceEndpointClient>(_serviceProvider, serviceEndpointClientOptions);

        IEnumerable<ServiceEndpoint> endpoints = await serviceEndpointClient.GetAll();

        ServiceEndpoint? endpointToUpdate = endpoints
            .FirstOrDefault(e => e.Name == name);

        if (endpointToUpdate is null)
        {
            _logger.LogWarning("No managed endpoint with name \"{EndpointName}\" found", name);
            return;
        }

        DateOnly expiresOnDate = DateOnly.FromDateTime(value.ExpiresOn.DateTime);
        DateOnly nextRotationDate = DateOnly.FromDateTime(value.NextRotationOn.DateTime);
        string description = ServiceConnectionMagicString.CreateMagicString(expiresOnDate, nextRotationDate);

        ServiceEndpointUpdateData updateData = new()
        {
            Description = description,
            AccessToken = value.Value
        };

        await serviceEndpointClient.Update(endpointToUpdate.Id, updateData);
    }
}
