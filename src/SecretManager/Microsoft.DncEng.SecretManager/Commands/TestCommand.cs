using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.Commands;

[Command("test")]
class TestCommand : ProjectBaseCommand
{
    private readonly IConsole _console;
    private readonly ITokenCredentialProvider _tokenProvider;

    public TestCommand(GlobalCommand globalCommand, IConsole console, ITokenCredentialProvider tokenProvider) : base(globalCommand)
    {
        _console = console;
        _tokenProvider = tokenProvider;
    }

    public override async Task RunAsync(CancellationToken cancellationToken)
    {
        WarnIfServiceTreeIdIsSetToEmptyGuid();

        var creds = await _tokenProvider.GetCredentialAsync();

        var token = await creds.GetTokenAsync(new TokenRequestContext(new[]
        {
            "https://servicebus.azure.net/.default",
        }), cancellationToken);

        Debug.WriteLine(token.ExpiresOn);
        _console.WriteImportant("Successfully authenticated");
    }
}
