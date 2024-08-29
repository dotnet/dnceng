using Microsoft.DncEng.SecretManager.ServiceConnections.RestModels;
using System;

namespace Microsoft.DncEng.SecretManager.ServiceConnections;

// Model of a Service Endpoint for application use
public class ServiceEndpoint
{
    public Guid Id { get; init; }
    public string Name { get; init; }
    public string Description { get; init; }
    public string Type { get; init; }

    public ServiceEndpoint(Guid id, string name, string description, string type)
    {
        Id = id;
        Name = name;
        Description = description;
        Type = type;
    }

    internal static ServiceEndpoint Create(AdoServiceEndpoint adoServiceEndpoint)
    {
        // The JSON schema allows more flexibility than used for the application model. We assume that there is only one element in the serviceEndpointProjectReferences array. We assume that the "name" and "description" elements in that array are what drive the info shown in the AzDO UI.

        return new ServiceEndpoint(
            adoServiceEndpoint.Id,
            adoServiceEndpoint.Name,
            adoServiceEndpoint.Description,
            adoServiceEndpoint.Type);
    }
}
