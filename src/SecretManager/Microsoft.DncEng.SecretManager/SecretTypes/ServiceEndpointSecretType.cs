using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Services.Security;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("azure-devops-service-endpoint")]
public class ServiceEndpointSecretType : SecretType<ServiceEndpointSecretType.Parameters>
{
    private readonly IServiceProvider _serviceProvider;

    public class Parameters
    {
        public AuthorizationParameters Authorization { get; set; }

        public class AuthorizationParameters
        {
            public string Type { get; set; }
            public AzureDevOpsAccessToken.Parameters Parameters { get; set; }
        }
    }

    // This secret type uses other secret types to perform actions. It gets access to those types via SecretTypeRegistry. Due to complications in how SecretTypeRegistry is constructed, we must use IServiceProvider get an instance of SecretTypeRegistry at runtime rather than inject directly to avoid creating a circular dependency. 
    public ServiceEndpointSecretType(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        SecretTypeRegistry secretTypeRegistry = _serviceProvider.GetRequiredService<SecretTypeRegistry>();
        AzureDevOpsAccessToken secretType = secretTypeRegistry.Get(parameters.Authorization.Type) as AzureDevOpsAccessToken;

        List<SecretData> secretDatas = await secretType.RotateValues(parameters.Authorization.Parameters, context, cancellationToken);

        SecretData secretData = secretDatas.Single();

        return secretData;
    }
}