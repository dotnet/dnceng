using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("github-access-token")]
public class GitHubAccessToken : GitHubAccountInteractiveSecretType<GitHubAccessToken.Parameters>
{
    // GitHub allows personal access tokens to be created with an expiration
    // between 1 and 366 days, but we require a minimum of 7 days.
    private const int _minExpirationInDays = 7;
    private const int _maxExpirationInDays = 366;

    public class Parameters
    {
        public string Name { get; set; }
        public SecretReference GitHubBotAccountSecret { get; set; }
        public string GitHubBotAccountName { get; set; }
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

        var expirationInDays = await Console.PromptAndValidateAsync<int>(
            "expiration in days",
            $"Expiration must be a whole number of days between {_minExpirationInDays} and {_maxExpirationInDays}.",
            TryParseExpirationInDays);

        var now = Clock.UtcNow;
        var expiresOn = now.AddDays(expirationInDays);
        var nextRotationOn = ComputeNextRotationOn(now, expirationInDays);

        const string helpUrl = "https://github.com/settings/tokens";
        Console.WriteLine($"When creating the new token, set the expiration to {expirationInDays}d in the future ({expiresOn:yyyy-MM-dd}).");
        await ShowGitHubLoginInformation(context, parameters.GitHubBotAccountSecret, helpUrl, parameters.GitHubBotAccountName);

        var pat = await Console.PromptAndValidateAsync("PAT",
            "PAT must have at least 40 characters.",
            value => value != null && value.Length >= 40);

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
}
