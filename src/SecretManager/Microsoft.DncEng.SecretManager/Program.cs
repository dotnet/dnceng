using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.SecretManager.Commands;
using Microsoft.DncEng.SecretManager.ServiceConnections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using System;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager;

public class Program : DependencyInjectedConsoleApp
{
    /// <summary>
    /// Object stores global command setting as parsed from the command line at the main method
    /// </summary>
    private static GlobalCommand _globalCommand;


    /// <summary>
    /// Object to extract the service tree as parsed from the command line at the main method
    /// This value is needed for dependency injection operations for security audit logging
    /// </summary>
    private static ProjectBaseCommand _projectBaselCommand;

    public static Task<int> Main(string[] args)
    {

        // The GlobalCommand must be pre-parsed before passing to the  ProjectBaseCommand object or the base global settings will be lost
        _globalCommand = new GlobalCommand();
        var options = _globalCommand.GetOptions();
        options.Parse(args);

        // We then parse the ProjectBaseCommand to ensure we collect the service tree id at the start of the progress
        // so it can be used for dependency ingjection
        // The global option setings are passed to all other command objects that inhearit from the ProjectBaseCommand
        _projectBaselCommand = new ProjectBaseCommand(_globalCommand);
        options = _projectBaselCommand.GetOptions();
        options.Parse(args);

        return new Program().RunAsync(args);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        // The injected service is needed to allow commands to consume global options set at the command line
        services.AddSingleton(_globalCommand);
        services.AddSingleton(new SecurityAuditLogger(_projectBaselCommand.ServiceTreeId));          
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