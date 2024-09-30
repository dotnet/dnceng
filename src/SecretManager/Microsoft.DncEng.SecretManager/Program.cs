using System;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.SecretManager.Commands;
using Microsoft.DncEng.SecretManager.ServiceConnections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Microsoft.DncEng.SecretManager;

public class Program : DependencyInjectedConsoleApp
{
    /// <summary>
    /// Object stores global command setting as parsed from the command line at the main method
    /// We mark this valud as protected so it can be accessed by processes that invoke the assembly outside of the command line
    /// </summary>
    protected static GlobalCommand _globalCommand = new GlobalCommand();


    /// <summary>
    /// The service tree id of calling service parsed from the command line at the main method
    /// We mark this valeue as protected so it can be accessed by processes that invoke the assembly outside of the command line
    /// </summary>
    protected static Guid ServiceTreeId = Guid.Empty;

    public static Task<int> Main(string[] args)
    {
        // The GlobalCommand must be pre-parsed before passing to the  ProjectBaseCommand object or the base global settings will be lost
        var options = _globalCommand.GetOptions();
        options.Parse(args);

        // We then parse the ProjectBaseCommand to ensure we collect the service tree id at the start of the progress
        // so it can be used for dependency ingjection
        // The global option setings are passed to all other command objects that inhearit from the ProjectBaseCommand
        var projectBaselCommand = new ProjectBaseCommand(_globalCommand);
        options = projectBaselCommand.GetOptions();
        options.Parse(args);
        ServiceTreeId = projectBaselCommand?.ServiceTreeId ?? Guid.Empty;

        return new Program().RunAsync(args);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        // The injected service is needed to allow commands to consume global options set at the command line
        services.AddSingleton(_globalCommand);
        services.AddSingleton(new SecurityAuditLogger(ServiceTreeId));          
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