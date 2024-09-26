using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.SecretManager.Commands;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager;

public class Program : DependencyInjectedConsoleApp
{
    // The ProjectBaseCommand is stored so it can be used to pass data to the depdency injection instructions
    private static ProjectBaseCommand _projectGlobalCommand;

    public static Task<int> Main(string[] args)
    {
        // The GlobalCommand must be pre-parsed before passing to the  ProjectBaseCommand object or the base global settings will be lost
        var globalCommand = new GlobalCommand();
        var options = globalCommand.GetOptions();
        options.Parse(args);

        // We then parse the ProjectBaseCommand to ensure we collect the project spacific for the service tree id at the start of the progress
        // so it can be used for dependency ingjection processes
        // The global option seting are stored and passed to all other command objects that inhearit from the ProjectBaseCommand
        _projectGlobalCommand = new ProjectBaseCommand(globalCommand);
        options = _projectGlobalCommand.GetOptions();
        options.Parse(args);

        return new Program().RunAsync(args);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(new SecurityAuditLogger(_projectGlobalCommand.ServiceTreeId));
        services.AddSingleton<GlobalCommand>(_projectGlobalCommand);
        services.AddSingleton<ITokenCredentialProvider, SecretManagerCredentialProvider>();
        services.AddSingleton<SecretTypeRegistry>();
        services.AddSingleton<StorageLocationTypeRegistry>();
        services.AddSingleton<SettingsFileValidator>();
        services.AddNamedFromAssembly<SecretType>(Assembly.GetExecutingAssembly());
        services.AddNamedFromAssembly<StorageLocationType>(Assembly.GetExecutingAssembly());
    }
}
