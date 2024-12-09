using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.SecretManager.Commands;

namespace Microsoft.DncEng.SecretManager;

[Command("info")]
class InfoCommand : CommonIdentityCommand
{
    private readonly IConsole _console;

    public InfoCommand(IConsole console): base()
    {
        _console = console;
    }

    public override Task RunAsync(CancellationToken cancellationToken)
    {
        var exeName = Process.GetCurrentProcess().ProcessName;
        var version = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "-.-.-.-";
        _console.WriteImportant($"{exeName} version {version}");
        return Task.CompletedTask;
    }
}
