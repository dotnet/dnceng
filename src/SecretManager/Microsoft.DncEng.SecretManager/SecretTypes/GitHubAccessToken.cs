using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("github-access-token")]
public class GitHubAccessToken : GitHubAccountInteractiveSecretType<GitHubAccessToken.Parameters>
{
    private const int _nextRotationOnDeltaMonths = 4;

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

        const string helpUrl = "https://github.com/settings/tokens";
        Console.WriteLine($"When creating the new token, please set the expiration date to at least {Clock.UtcNow.AddMonths(_nextRotationOnDeltaMonths + 1).ToString("yyyy-MM-dd")} this is what the SecretManager metadata will be set to");
        await ShowGitHubLoginInformation(context, parameters.GitHubBotAccountSecret, helpUrl, parameters.GitHubBotAccountName);

        var pat = await Console.PromptAndValidateAsync("PAT",
            "PAT must have at least 40 characters.",
            value => value != null && value.Length >= 40);

        return new SecretData(pat, DateTimeOffset.MaxValue, Clock.UtcNow.AddMonths(_nextRotationOnDeltaMonths));
    }
}
