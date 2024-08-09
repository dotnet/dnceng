using System;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.SecretManager.ServiceConnections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Microsoft.DncEng.SecretManager;

public class Program : DependencyInjectedConsoleApp
{
    public static Task<int> Main(string[] args)
    {
        return new Program().RunAsync(args);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
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

        services.AddSingleton<ServiceEndpointClient>();
    }
}