using ConsoleTables;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.SecretManager.StorageTypes;
using Microsoft.VisualStudio.Services.Common;
using Mono.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Command = Microsoft.DncEng.CommandLineLib.Command;

#nullable enable

namespace Microsoft.DncEng.SecretManager.Commands;

[Command("stats", Description = "Emit statistics about supplied manifests")]
internal class StatsCommand : Command
{
    // Configuration info
    // This list should be updated to include any SecretType<T> that might throw HumanInterventionRequiredException.
    private readonly List<string> _humanRequiredTypes =
    [
        "ad-application",
        "domain-account",
        "github-access-token",
        "github-app-secret",
        "github-account",
        "github-oauth-secret",
        "sql-connection-string",
        "text"
    ];

    // Provided by CLI flags
    private bool _includeExpiration = false;
    private readonly List<string> _manifestFiles = [];
    private readonly List<string> _manifestDirectories = [];

    // Provided by DI
    private readonly StorageLocationTypeRegistry _storageLocationTypeRegistry;
    private readonly IConsole _console;

    public StatsCommand(StorageLocationTypeRegistry storageLocationTypeRegistry, IConsole console)
    {
        _storageLocationTypeRegistry = storageLocationTypeRegistry;
        _console = console;
    }

    public override OptionSet GetOptions()
    {
        return base.GetOptions().AddRange(new OptionSet()
        {
            {"m|manifest-file=", "The secret manifest file", f => _manifestFiles.Add(f)},
            {"p|manifest-path=", "A directory containing one or more manifests", f => _manifestDirectories.Add(f)},
            {"x|include-expiration", "If set, also determine expiration date of matching secrets", _ => _includeExpiration = true }
        });
    }

    public override bool AreRequiredOptionsSet()
    {
        return (_manifestFiles.Count + _manifestDirectories.Count) > 0;
    }

    public override async Task RunAsync(CancellationToken cancellationToken)
    {
        IEnumerable<string> missingManifestFiles = _manifestFiles
            .Where(f => !File.Exists(f));

        if (missingManifestFiles.Any())
        {
            throw new FailWithExitCodeException(86, $"The following manifest files were not found: {Environment.NewLine} {string.Join(Environment.NewLine, missingManifestFiles)}");
        }

        IEnumerable<string> missingOrEmptyManifestPaths = _manifestDirectories
                .Where(d => !Directory.EnumerateFiles(d, "*.yaml", SearchOption.TopDirectoryOnly).Any());

        if (missingOrEmptyManifestPaths.Any())
        {
            throw new FailWithExitCodeException(86, $"The following manifest directories were not found or contained no manifests: {Environment.NewLine} {string.Join(Environment.NewLine, missingOrEmptyManifestPaths)}");
        }

        _manifestFiles.AddRange(
            _manifestDirectories
                .SelectMany(d => Directory.EnumerateFiles(d, "*.yaml", SearchOption.TopDirectoryOnly))
        );

        int totalEntriesCount = 0;
        List<HumanRequiredSecretsWithExpiryRow> rows = [];
        foreach (string manifestFile in _manifestFiles)
        {
            SecretManifest manifest = SecretManifest.Read(manifestFile);

            totalEntriesCount += manifest.Secrets.Count;

            Dictionary<string, SecretManifest.Secret> humanRequiredSecrets = manifest.Secrets
                .Where(secret => _humanRequiredTypes.Contains(secret.Value.Type))
                .ToDictionary(x => x.Key, x => x.Value);

            if (humanRequiredSecrets.Count == 0)
            {
                continue;
            }

            if (!_includeExpiration)
            {
                rows.AddRange(humanRequiredSecrets.Select(secret => new HumanRequiredSecretsWithExpiryRow(manifestFile, secret.Key, secret.Value.Type, null)));

                continue;
            }

            _console.WriteLine($"Gathering expiration for {humanRequiredSecrets.Count} secrets in {manifestFile}");

            using StorageLocationType.Bound storage = _storageLocationTypeRegistry
                .Get(manifest.StorageLocation.Type).BindParameters(manifest.StorageLocation.Parameters);

            Dictionary<string, SecretProperties> existingSecrets = (await storage.ListSecretsAsync()).ToDictionary(p => p.Name);

            foreach (KeyValuePair<string, SecretManifest.Secret> secret in humanRequiredSecrets)
            {
                bool secretFoundInVault = existingSecrets.TryGetValue(secret.Key, out SecretProperties? existingSecretProperties);

                if (!secretFoundInVault || existingSecretProperties is null)
                {
                    rows.Add(new HumanRequiredSecretsWithExpiryRow(manifestFile, secret.Key, secret.Value.Type, "Not found in vault"));

                    continue;
                }

                bool secretHasExpirationTag = existingSecretProperties.Tags.TryGetValue(AzureKeyVault.NextRotationOnTag, out string? nextRotationOnValue);

                if (!secretHasExpirationTag || nextRotationOnValue is null)
                {
                    rows.Add(new HumanRequiredSecretsWithExpiryRow(manifestFile, secret.Key, secret.Value.Type, "No expiration tag"));

                    continue;
                }

                rows.Add(new HumanRequiredSecretsWithExpiryRow(manifestFile, secret.Key, secret.Value.Type, nextRotationOnValue));
            }
        }

        if (!rows.Any())
        {
            Console.WriteLine("No human required secrets found");
        }

        if (_includeExpiration)
        {
            ConsoleTable.From(rows)
                .Write(Format.Minimal);
        }
        else
        {
            ConsoleTable.From(rows.Select(r => new { r.Manifest, r.Name, r.Type }))
                .Write(Format.Minimal);
        }

        _console.WriteLine(string.Empty);
        _console.WriteLine($"Total entries across all included manifests: {totalEntriesCount}");

        return;
    }

    private record HumanRequiredSecretsWithExpiryRow(string Manifest, string Name, string Type, string? Expiration);
}
