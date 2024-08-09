using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.DncEng.SecretManager.ServiceConnections.RestModels;

#nullable enable

// Model docs: https://learn.microsoft.com/en-us/rest/api/azure/devops/serviceendpoint/endpoints/get?view=azure-devops-rest-7.2&tabs=HTTP#serviceendpoint
internal class AdoServiceEndpoint
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Type { get; set; }

    public ServiceEndpointProjectReference[] ServiceEndpointProjectReferences { get; set; }

    public EndpointAuthorization Authorization { get; set; }

    [JsonExtensionData]
    public IDictionary<string, object>? AdditionalData { get; set; }

    public AdoServiceEndpoint(Guid id, string name, string description, string type, ServiceEndpointProjectReference[] serviceEndpointProjectReferences)
    {
        Id = id;
        Name = name;
        Description = description;
        Type = type;
        ServiceEndpointProjectReferences = serviceEndpointProjectReferences;
    }
}

internal class ServiceEndpointProjectReference
{
    public string Name { get; set; }
    public string? Description { get; set; }
    public ProjectReference ProjectReference { get; set; }

    public ServiceEndpointProjectReference(string name, ProjectReference projectReference)
    {
        Name = name;
        ProjectReference = projectReference;
    }
}

/// <seealso href="https://learn.microsoft.com/en-us/rest/api/azure/devops/serviceendpoint/endpoints/get?view=azure-devops-rest-7.2&tabs=HTTP#projectreference">Endpoint docs</seealso>
internal class ProjectReference
{
    public Guid Id { get; set; }
    public string Name { get; set; }

    public ProjectReference(Guid id, string name)
    {
        Id = id;
        Name = name;
    }
}

internal class EndpointAuthorization
{
    public string Scheme { get; init;}

    public EndpointAuthorization(string scheme)
    {
        Scheme = scheme;
    }
}