using Microsoft.DncEng.CommandLineLib;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager.SecretTypes
{
    public abstract class GitHubAccountInteractiveSecretType<TParameters> : SecretType<TParameters>
        where TParameters : new()
    {
        protected const string GitHubPasswordSuffix = "-password";
        protected const string GitHubSecretSuffix = "-secret";
        protected const string GitHubRecoveryCodesSuffix = "-recovery-codes";

        protected ISystemClock Clock { get; }
        protected IConsole Console { get; }

        public GitHubAccountInteractiveSecretType(ISystemClock clock, IConsole console)
        {
            Clock = clock;
            Console = console;
        }

        protected async Task ShowGitHubLoginInformation(RotationContext context, string gitHubSecretName, string gitHubAccountName)
        {
            var password = await context.GetSecretValue(gitHubSecretName + GitHubPasswordSuffix);
            var secret = await context.GetSecretValue(gitHubSecretName + GitHubSecretSuffix);

            await ShowGitHubLoginInformation(gitHubAccountName, password, secret);
        }

        protected async Task ShowGitHubLoginInformation(string gitHubAccountName, string gitHubPassword, string gitHubSecret)
        {
            Console.WriteLine($"Please login to GitHub account {gitHubAccountName} using password: {gitHubPassword}");
            await ShowGitHubOneTimePassword(gitHubSecret);
        }

        protected async Task ShowGitHubOneTimePassword(string secret)
        {
            var passwordGenerator = new OneTimePasswordGenerator(secret);
            var generateTotp = true;
            while (generateTotp)
            {
                var oneTimePassword = passwordGenerator.Generate(Clock.UtcNow);
                generateTotp = await Console.ConfirmAsync($"Your one time password: {oneTimePassword}. Enter yes to generate another one: ");
            }
        }
    }
}