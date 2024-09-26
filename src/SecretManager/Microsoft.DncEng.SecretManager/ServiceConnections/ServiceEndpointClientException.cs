using System;

namespace Microsoft.DncEng.SecretManager.ServiceConnections;

#nullable enable

public class ServiceEndpointClientException : Exception
{
    public ServiceEndpointClientException()
    {

    }

    public ServiceEndpointClientException(string? message) : base(message)
    {

    }

    public ServiceEndpointClientException(string? message, Exception? innerException) : base(message, innerException)
    {

    }
}
