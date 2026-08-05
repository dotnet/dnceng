using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("github-access-token")]
public class GitHubAccessToken : GitHubAccountInteractiveSecretType<GitHubAccessToken.Parameters>
{
    private const string _classicTokenPrefix = "ghp_";
    private const string _fineGrainedTokenPrefix = "github_pat_";
    // GitHub allows a higher maximum, but we deliberately restrict access token
    // lifetimes to between 7 and 30 days.
    private const int _minExpirationInDays = 7;
    private const int _maxExpirationInDays = 30;

    public class Parameters
    {
        public string Name { get; set; }
        public SecretReference GitHubBotAccountSecret { get; set; }
        public string GitHubBotAccountName { get; set; }
        public string Description { get; set; }
    }

    public GitHubAccessToken(ISystemClock clock, IConsole console) : base(clock, console)
    {
    }

    protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        if (!Console.IsInteractive)
        {
            throw new HumanInterventionRequiredException($"User intervention required for creation or rotation of a GitHub access token.");
        }

        int expirationInDays = await Console.PromptAndValidateAsync<int>(
            "expiration in days",
            $"Expiration must be a whole number of days between {_minExpirationInDays} and {_maxExpirationInDays}.",
            TryParseExpirationInDays);

        DateTimeOffset now = Clock.UtcNow;
        DateTimeOffset expiresOn = now.AddDays(expirationInDays);
        DateTimeOffset nextRotationOn = ComputeNextRotationOn(now, expirationInDays);

        const string helpUrl = "https://github.com/settings/tokens";

        if (!string.IsNullOrEmpty(parameters.Description))
        {
            Console.WriteLine($"Description: {parameters.Description}");
        }
        await ShowGitHubLoginInformation(context, parameters.GitHubBotAccountSecret, helpUrl, parameters.GitHubBotAccountName);

        string pat = await Console.PromptAndValidateAsync("PAT",
            $"PAT must have at least 40 characters and start with either '{_classicTokenPrefix}' or '{_fineGrainedTokenPrefix}'.",
            ValidatePat);

        Console.WriteLine($"Next rotation was set to {nextRotationOn:yyyy-MM-dd}.");

        return new SecretData(pat, expiresOn, nextRotationOn);
    }

    // Rotate once roughly two thirds of the way through the token's lifetime,
    // i.e. when about one third of the entered duration remains before expiration.
    protected static DateTimeOffset ComputeNextRotationOn(DateTimeOffset now, int expirationInDays)
    {
        return now.AddDays(expirationInDays * 2 / 3);
    }

    protected static bool TryParseExpirationInDays(string value, out int parsedValue)
    {
        return int.TryParse(value, out parsedValue)
            && parsedValue >= _minExpirationInDays
            && parsedValue <= _maxExpirationInDays;
    }

    private static bool ValidatePat(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 40)
        {
            return false;
        }

        // ghp_ prefix indicates a classic personal access token.
        // github_pat_ prefix indicates a fine-grained personal access token.
        return value.StartsWith(_classicTokenPrefix, StringComparison.Ordinal)
            || value.StartsWith(_fineGrainedTokenPrefix, StringComparison.Ordinal);
    }
}
