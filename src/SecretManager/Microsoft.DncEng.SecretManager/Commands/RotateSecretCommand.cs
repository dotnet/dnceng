using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.SecretManager.StorageTypes;
using Microsoft.VisualStudio.Services.Common;
using Mono.Options;
using Command = Microsoft.DncEng.CommandLineLib.Command;

namespace Microsoft.DncEng.SecretManager.Commands;

[Command("rotate-secret", Description = "Rotate a single secret from a manifest file.")]
public class RotateSecretCommand : Command
{
    /// <summary>
    /// Provides the ServiceTreeId set with global options.
    /// The ID is a guid and is set to the Helix service tree ID by default.
    /// </summary>
    private Guid ServiceTreeId { get; set; } = new Guid("8835b1f3-0d22-4e28-bae0-65da04655ed4");

    private readonly StorageLocationTypeRegistry _storageLocationTypeRegistry;
    private readonly SecretTypeRegistry _secretTypeRegistry;
    private readonly IConsole _console;

    private string _manifestFile;
    private string _secretName;
    private bool _yes;

    public RotateSecretCommand(
        StorageLocationTypeRegistry storageLocationTypeRegistry,
        SecretTypeRegistry secretTypeRegistry,
        IConsole console)
    {
        _storageLocationTypeRegistry = storageLocationTypeRegistry;
        _secretTypeRegistry = secretTypeRegistry;
        _console = console;
    }

    public override List<string> HandlePositionalArguments(List<string> args)
    {
        _manifestFile = ConsumeIfNull(_manifestFile, args);
        return base.HandlePositionalArguments(args);
    }

    public override OptionSet GetOptions()
    {
        return base.GetOptions().AddRange(new OptionSet()
        {
            {"servicetreeid=", "Your service tree ID (Ids are defined at aka.ms/servicetree)", id =>
                {
                    if (Guid.TryParse(id, out var guid))
                    {
                        ServiceTreeId = guid;
                    }
                    else
                    {
                        throw new ArgumentException($"Failed to parse a valid Guid value from ServiceTreeId value '{id}'!");
                    }
                }
            },
            {"s|secret=", "The name of the secret to rotate (as listed in the manifest).", s => _secretName = s},
            {"y|yes", "Skip the confirmation prompt.", y => _yes = !string.IsNullOrEmpty(y)},
        });
    }

    public override bool AreRequiredOptionsSet()
    {
        return !string.IsNullOrEmpty(_manifestFile) && !string.IsNullOrEmpty(_secretName);
    }

    public override async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            _console.WriteLine($"🔁 Rotating secret '{_secretName}' from {_manifestFile}");

            if (!_yes)
            {
                bool confirmed = await _console.ConfirmAsync(
                    $"This will rotate secret '{_secretName}' ahead of schedule, possibly causing service disruption. Continue? (yes/no): ");
                if (!confirmed)
                {
                    return;
                }
            }

            SecretManifest manifest = SecretManifest.Read(_manifestFile);

            if (!manifest.Secrets.TryGetValue(_secretName, out SecretManifest.Secret secret))
            {
                _console.LogError($"Secret '{_secretName}' was not found in manifest {_manifestFile}.");
                throw new FailWithExitCodeException(1);
            }

            using StorageLocationType.Bound storage = _storageLocationTypeRegistry
                .Get(manifest.StorageLocation.Type)
                .BindParameters(manifest.StorageLocation.Parameters);
            storage.SetSecurityAuditLogger(new SecurityAuditLogger(ServiceTreeId));

            using var disposables = new DisposableList();
            var references = new Dictionary<string, StorageLocationType.Bound>();
            foreach (var (name, storageReference) in manifest.References)
            {
                var bound = _storageLocationTypeRegistry.Get(storageReference.Type)
                    .BindParameters(storageReference.Parameters);
                disposables.Add(bound);
                references.Add(name, bound);
            }

            SecretType.Bound secretType = _secretTypeRegistry.Get(secret.Type).BindParameters(secret.Parameters);

            Dictionary<string, SecretProperties> existingSecrets = (await storage.ListSecretsAsync())
                .ToDictionary(p => p.Name);

            List<string> names = secretType.GetCompositeSecretSuffixes()
                .Select(suffix => _secretName + suffix)
                .ToList();

            SecretProperties primary = null;
            foreach (string n in names)
            {
                if (existingSecrets.TryGetValue(n, out var e) && primary == null)
                {
                    primary = e;
                }
            }

            IImmutableDictionary<string, string> currentTags = primary?.Tags
                ?? ImmutableDictionary.Create<string, string>();

            _console.WriteLine($"Generating new value(s) for secret {_secretName}...");
            var context = new RotationContext(_secretName, currentTags, storage, references);
            List<SecretData> newValues = await secretType.RotateValues(context, cancellationToken);
            IImmutableDictionary<string, string> newTags = context.GetValues();
            _console.WriteLine("Done.");

            _console.WriteLine($"Storing new value(s) in storage for secret {_secretName}...");
            foreach (var (n, value) in names.Zip(newValues))
            {
                await storage.SetSecretValueAsync(n, new SecretValue(value.Value, newTags, value.NextRotationOn, value.ExpiresOn));
            }
            _console.WriteLine("Done.");

            if (newTags.TryGetValue(AzureKeyVault.NextRotationOnTag, out string nextRotationOn))
            {
                _console.WriteLine($"✅ Rotated secret '{_secretName}' - next rotation on {nextRotationOn}.");
            }
            else
            {
                _console.WriteLine($"✅ Rotated secret '{_secretName}'.");
            }
        }
        catch (FailWithExitCodeException)
        {
            throw;
        }
        catch (HumanInterventionRequiredException hire)
        {
            _console.LogError(hire.Message);
            throw new FailWithExitCodeException(42);
        }
        catch (Exception ex)
        {
            _console.LogError($"Unhandled Exception: {ex}");
            throw new FailWithExitCodeException(-1);
        }
    }
}
