using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.Commands;

[Command("test")]
class TestCommand : CommonIdentityCommand
{
    private readonly IConsole _console;
    private readonly ITokenCredentialProvider _tokenProvider;

    public TestCommand(IConsole console, ITokenCredentialProvider tokenProvider) : base()
    {
        _console = console;
        _tokenProvider = tokenProvider;
    }

    public override async Task RunAsync(CancellationToken cancellationToken)
    {
        var creds = await _tokenProvider.GetCredentialAsync();

        var token = await creds.GetTokenAsync(new TokenRequestContext(new[]
        {
            "https://servicebus.azure.net/.default",
        }), cancellationToken);

        Debug.WriteLine(token.ExpiresOn);
        _console.WriteImportant("Successfully authenticated");
    }
}
