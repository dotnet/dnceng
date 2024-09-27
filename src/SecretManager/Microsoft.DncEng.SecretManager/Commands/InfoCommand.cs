using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.SecretManager.Commands;

namespace Microsoft.DncEng.SecretManager;

[Command("info")]
class InfoCommand : ProjectBaseCommand
{
    private readonly IConsole _console;

    public InfoCommand(GlobalCommand globalCommand, IConsole console): base(globalCommand)
    {
        _console = console;
    }

    public override Task RunAsync(CancellationToken cancellationToken)
    {
        // Provides a curtisy warning message if the ServiceTreeId option is set to a empty guid
        ValidateServiceTreeIdOption();

        var exeName = Process.GetCurrentProcess().ProcessName;
        var version = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "-.-.-.-";
        _console.WriteImportant($"{exeName} version {version}");
        return Task.CompletedTask;
    }
}
