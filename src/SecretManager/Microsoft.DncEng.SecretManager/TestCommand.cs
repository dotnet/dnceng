using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.CommandLineLib.Authentication;
using Microsoft.DncEng.Configuration.Extensions;

namespace Microsoft.DncEng.SecretManager;

[Command("test")]
class TestCommand : Command
{
    private readonly IConsole _console;
    private readonly TokenCredentialProvider _tokenProvider;

    public TestCommand(IConsole console, TokenCredentialProvider tokenProvider)
    {
        _console = console;
        _tokenProvider = tokenProvider;
    }

    public override async Task RunAsync(CancellationToken cancellationToken)
    {
        var creds = await _tokenProvider.GetCredentialAsync();

        creds = new ChainedTokenCredential(creds, new AzureCliCredential(new AzureCliCredentialOptions() { TenantId = ConfigurationConstants.MsftAdTenantId }));
        var token = await creds.GetTokenAsync(new TokenRequestContext(new []
        {
            "https://servicebus.azure.net/.default",
        }), cancellationToken);
        Debug.WriteLine(token.ExpiresOn);
        _console.WriteImportant("Successfully authenticated");
    }
}
