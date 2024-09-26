using System;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.SecretManager.ServiceConnections;
using Microsoft.Extensions.Azure;
using Microsoft.DncEng.SecretManager.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager;

public class Program : DependencyInjectedConsoleApp
{
    // The ProjectBaseCommand is stored so it can be used to pass data to the depdency injection instructions
    private static ProjectBaseCommand _projectGlobalCommand;

    public static Task<int> Main(string[] args)
    {
        // The GlobalCommand must be pre-parsed before passing to the  ProjectBaseCommand object or the base global settings will be lost
        var globalCommand = new GlobalCommand();
        var options = globalCommand.GetOptions();
        options.Parse(args);

        // We then parse the ProjectBaseCommand to ensure we collect the project spacific for the service tree id at the start of the progress
        // so it can be used for dependency ingjection processes
        // The global option seting are stored and passed to all other command objects that inhearit from the ProjectBaseCommand
        _projectGlobalCommand = new ProjectBaseCommand(globalCommand);
        options = _projectGlobalCommand.GetOptions();
        options.Parse(args);

        return new Program().RunAsync(args);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(new SecurityAuditLogger(_projectGlobalCommand.ServiceTreeId));
        services.AddSingleton<GlobalCommand>(_projectGlobalCommand);
        services.AddSingleton<ITokenCredentialProvider, SecretManagerCredentialProvider>();
        services.AddSingleton<SecretTypeRegistry>();
        services.AddSingleton<StorageLocationTypeRegistry>();
        services.AddSingleton<SettingsFileValidator>();
        services.AddNamedFromAssembly<SecretType>(Assembly.GetExecutingAssembly());
        services.AddNamedFromAssembly<StorageLocationType>(Assembly.GetExecutingAssembly());

        services.AddHttpClient();
        services.Configure<HttpClientFactoryOptions>(factory =>
        {
            factory.HttpMessageHandlerBuilderActions.Add(handlerBuilder =>
            {
                if (handlerBuilder.PrimaryHandler is SocketsHttpHandler socketsHttpHandler)
                {
                    socketsHttpHandler.SslOptions.CertificateRevocationCheckMode = X509RevocationMode.Online;
                }
                else if (handlerBuilder.PrimaryHandler is HttpClientHandler httpClientHandler)
                {
                    httpClientHandler.CheckCertificateRevocationList = true;
                }
                else
                {
                    throw new InvalidOperationException($"Could not create client with CRL check, HttpMessageHandler type {handlerBuilder.PrimaryHandler.GetType().FullName ?? handlerBuilder.PrimaryHandler.GetType().Name} is unknown.");
                }
            });
        });

        services.Configure<ServiceEndpointClient.Configuration>(config => {});
        services.AddSingleton<ServiceEndpointClient>();
    }
}