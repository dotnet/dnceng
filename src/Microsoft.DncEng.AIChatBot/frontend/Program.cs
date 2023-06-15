// Copyright (c) Microsoft. All rights reserved.

// [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("ClientApp.Tests")]
namespace ClientApp;

internal class Program
{
    public static async Task Main()
    {

        var builder = WebAssemblyHostBuilder.CreateDefault();

        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        builder.Services.Configure<AppSettings>(
            builder.Configuration.GetSection(nameof(AppSettings)));
        builder.Services.AddHttpClient<ApiClient>(client =>
        {
            client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
        });
        builder.Services.AddScoped<OpenAIPromptQueue>();
        builder.Services.AddLocalStorageServices();
        builder.Services.AddSessionStorageServices();
        builder.Services.AddSpeechSynthesisServices();
        builder.Services.AddSpeechRecognitionServices();
        builder.Services.AddMudServices();

        // await JSHost.ImportAsync(
        //     moduleName: nameof(JavaScriptModule),
        //     moduleUrl: $"../js/iframe.js?{Guid.NewGuid()}" /* cache bust */);

        var host = builder.Build();
        await host.RunAsync();
        
    }
}