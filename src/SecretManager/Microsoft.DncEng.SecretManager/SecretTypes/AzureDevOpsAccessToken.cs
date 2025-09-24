using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.VisualStudio.Services.Account.Client;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.DelegatedAuthorization;
using Microsoft.VisualStudio.Services.DelegatedAuthorization.WebApi;
using Microsoft.VisualStudio.Services.OAuth;
using Microsoft.VisualStudio.Services.Profile;
using Microsoft.VisualStudio.Services.Profile.Client;
using Microsoft.VisualStudio.Services.WebApi;
using AzureCore = Azure.Core;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("azure-devops-access-token")]
public class AzureDevOpsAccessToken : SecretType<AzureDevOpsAccessToken.Parameters>
{
    public class Parameters
    {
        public string Organizations { get; set; }
        public SecretReference DomainAccountSecret { get; set; }
        public string DomainAccountName { get; set; }
        public string Scopes { get; set; }
    }

    public ISystemClock Clock { get; }
    public IConsole Console { get; }

    public AzureDevOpsAccessToken(ISystemClock clock, IConsole console)
    {
        Clock = clock;
        Console = console;
    }

    private const string msftTenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47";
    // Note that the below two GUIDs are for VSTS resource ID and Azure Powershell Client ID. Do not modify.
    private const string ClientId = "1950a258-227b-4e31-a9cf-717495945fc2";
    private const string VstsResourceId = "499b84ac-1321-427f-aa17-267ca6975798";

    private static async Task<VssConnection> ConnectToAzDo(string userName, string password, CancellationToken cancellationToken)
    {
        UsernamePasswordCredential credential = new(userName, password, msftTenantId, ClientId);
        TokenRequestContext requestContext = new([VstsResourceId + "/.default"]);
        AzureCore.AccessToken result = await credential.GetTokenAsync(requestContext, cancellationToken);

        string baseUri = "https://app.vssps.visualstudio.com";
        VssConnection connection = new (new Uri(baseUri), new VssCredentials(new VssOAuthAccessTokenCredential(result.Token)));

        return connection;
    }

    protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(parameters.Organizations))
        {
            throw new ArgumentException("Organizations is required.");
        }

        if (string.IsNullOrEmpty(parameters.Scopes))
        {
            throw new ArgumentException("Scopes is required.");
        }

        var orgs = parameters.Organizations.Split(' ');

        var userName = parameters.DomainAccountName;
        if (!userName.EndsWith("@microsoft.com"))
        {
            userName += "@microsoft.com";
        }
        
        var password = await context.GetSecretValue(parameters.DomainAccountSecret);

        using var connection = await ConnectToAzDo(userName, password, cancellationToken);

        Console.WriteLine("Connecting to AzDo and retrieving account guids.");
        
        var profileClient = await connection.GetClientAsync<ProfileHttpClient>(cancellationToken);
        var accountClient = await connection.GetClientAsync<AccountHttpClient>(cancellationToken);
        var tokenClient = await connection.GetClientAsync<TokenHttpClient>(cancellationToken);

        var me = await profileClient.GetProfileAsync(new ProfileQueryContext(AttributesScope.Core), cancellationToken: cancellationToken);

        foreach (var prop in me.GetType().GetProperties())
        {
            Console.WriteLine($"{prop.Name}: {prop.GetValue(me)}");
        }

        var accounts = await accountClient.GetAccountsByMemberAsync(me.Id, cancellationToken: cancellationToken);

        // This effort to manually add info about mseng is a workaround. See dotnet/dnceng#5851.
        bool msengOrgRequested = orgs.Contains("mseng", StringComparer.OrdinalIgnoreCase);

        bool msengOrgInDiscoveredAccounts = accounts
            .Where(account => account.AccountName.Equals("mseng", StringComparison.OrdinalIgnoreCase))
            .Any();

        if (msengOrgRequested && !msengOrgInDiscoveredAccounts)
        {
            Console.LogWarning("The 'mseng' organization was not found in the list of accounts and will be explicitly added. This is a work-around for dotnet/dnceng#5851.");
            VisualStudio.Services.Account.Account msengAccount = new(Guid.Parse("0efb4611-d565-4cd1-9a64-7d6cb6d7d5f0"))
            {
                AccountName = "mseng"
            };

            accounts.Add(msengAccount);
        }

        var accountGuidMap = accounts.ToDictionary(account => account.AccountName, account => account.AccountId, StringComparer.OrdinalIgnoreCase);
        // Print all account names and their GUIDs
        foreach (var kvp in accountGuidMap)
        {
            Console.WriteLine($"Account: {kvp.Key}, GUID: {kvp.Value}");
        }
        var orgIds = orgs.Select(name => accountGuidMap[name]).ToArray();
        foreach(var orgId in orgIds)
        {
            Console.WriteLine($"OrgID {orgId}");
        }
        var now = Clock.UtcNow;

        var scopes = parameters.Scopes
            .Split(' ')
            .Select(s => s.StartsWith("vso.") ? s : $"vso.{s}")
            .ToArray();

        Console.WriteLine($"Creating new pat in orgs '{string.Join(" ", orgIds)}' with scopes '{string.Join(" ", scopes)}'");
        var rotatesOn = now.AddDays(1);
        var expiresOn = now.AddDays(3);
        var newToken = await tokenClient.CreateSessionTokenAsync(new SessionToken
        {
            DisplayName = $"{context.SecretName} {now:u}",
            Scope = string.Join(" ", scopes),
            ValidFrom = now.UtcDateTime,
            ValidTo = expiresOn.UtcDateTime,
            TargetAccounts = orgIds,
        }, cancellationToken: cancellationToken);

        if (expiresOn - newToken.ValidTo > TimeSpan.FromDays(1))
        {
            Console.LogWarning($"Issued token expires on {newToken.ValidTo}, which is more than 1 day from the requested duration of {expiresOn}. This is unexpected and may disrupt secret management.");
        }

        return new SecretData(newToken.Token, expiresOn, rotatesOn);
    }
}
