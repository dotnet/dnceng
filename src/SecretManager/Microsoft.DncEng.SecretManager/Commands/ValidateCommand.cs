using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.VisualStudio.Services.Common;
using Mono.Options;
using Command = Microsoft.DncEng.CommandLineLib.Command;

namespace Microsoft.DncEng.SecretManager.Commands;

[Command("validate")]
public class ValidateCommand : ProjectBaseCommand
{
    private readonly SettingsFileValidator _settingsFileValidator;
    private string _manifestFile;
    private string _baseSettingsFile;
    private string _envSettingsFile;

    public ValidateCommand(GlobalCommand globalCommand, SettingsFileValidator settingsFileValidator) : base(globalCommand)
    {
        _settingsFileValidator = settingsFileValidator;
    }

    public override OptionSet GetOptions()
    {
        return base.GetOptions().AddRange(new OptionSet()
        {
            {"m|manifest-file=", "The secret manifest file", f => _manifestFile = f},
            {"e|env-settings-file=", "The environment settings file to validate", f => _envSettingsFile = f},
            {"b|base-settings-file=", "The base settings file to validate", f => _baseSettingsFile = f},
        });
    }

    public override bool AreRequiredOptionsSet()
    {
        return !string.IsNullOrEmpty(_manifestFile) &&
               !string.IsNullOrEmpty(_envSettingsFile) &&
               !string.IsNullOrEmpty(_baseSettingsFile);
    }

    public override async Task RunAsync(CancellationToken cancellationToken)
    {
        // Provides a curtisy warning message if the ServiceTreeId option is set to a empty guid
        ValidateServiceTreeIdOption();
        bool foundError = !await _settingsFileValidator.ValidateFileAsync(_envSettingsFile, _baseSettingsFile, _manifestFile, cancellationToken);

        if (foundError)
        {
            throw new FailWithExitCodeException(76);
        }
    }
}
