
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
    /// Command args are stored for future use to parse options for the CommonIdentityCommand
    private static string[] Args = new string[] { };

    public static Task<int> Main(string[] args)
    {
        Args = args;
        return new Program().RunAsync(args);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        //// Dependency injection instruction needed to support properties used for Geneva Logging operations
        services.AddSingleton<CommonIdentityCommand>();
        services.AddSingleton(serviceProvider =>
        {
            var baseCommand = serviceProvider.GetRequiredService<CommonIdentityCommand>();
            // We pre-pars command argument here to overcome a order of operatoins issue where 
            // the SecurityAuditLogger object is instantiated before normal command options would be parsed
            // by DependencyInjectedConsoleApp RunAsync processes employed by command objects
            // In shoret we do this becasue we need the value for the IDs before they would be read in normal Command processing
            var options = baseCommand.GetOptions();
            options.Parse(Args);
            return new SecurityAuditLogger(baseCommand);
        });


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

        services.Configure<ServiceEndpointClient.Configuration>(config => { });
        services.AddSingleton<ServiceEndpointClient>();
    }
}