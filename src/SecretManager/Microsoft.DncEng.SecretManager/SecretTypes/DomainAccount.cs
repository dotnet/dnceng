using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("domain-account")]
public class DomainAccount : SecretType<DomainAccount.Parameters>
{
    public class Parameters
    {
        public string AccountName { get; set; }
        public string Description { get; set; }
    }

    private readonly ISystemClock _clock;
    private readonly IConsole _console;

    public DomainAccount(ISystemClock clock, IConsole console)
    {
        _clock = clock;
        _console = console;
    }

    protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        if (!_console.IsInteractive)
        {
            throw new HumanInterventionRequiredException($"User intervention required for creation or rotation of a Domain Account.");
        }

        string password = await context.GetSecretValue(new SecretReference(context.SecretName));
        if (!string.IsNullOrEmpty(password))
            _console.WriteLine($"Current password for account {parameters.AccountName}: {password}");

        _console.WriteLine($@"Steps:
1. Visit https://coreidentity.microsoft.com/manage/service and review expiration dates for this bot account.
   Will need to click on the name link (e.g. https://coreidentity.microsoft.com/manage/Service/redmond/dn-bot)
   for the bot account to see both password and account expiration dates.
2. Click on 'Extend' if the account itself is expired or will expire soon.
3. Click on 'Reset Password' if the password is expired or will expire soon.
4. Copy the generated password to the clipboard before doing anything else. Paste it when requested in step 5.
5. Run 'secret-manager --force-secret={parameters.AccountName}' to update the key vault with the new password.");

        if (!string.IsNullOrWhiteSpace(parameters.Description))
            _console.WriteLine($"Additional information: {parameters.Description}");

        string newPassword = await _console.PromptAndValidateAsync("New Password",
            "Expecting a valid password from CoreIdentity, containing at least 20 fairly random characters",
            value => value != null && value.Length >= 20 && !value.All(char.IsLetterOrDigit));

        return new SecretData(newPassword, DateTimeOffset.MaxValue, _clock.UtcNow.AddMonths(6));
    }
}
