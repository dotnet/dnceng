// See https://aka.ms/new-console-template for more information
using Microsoft.DncEng.SecretManager;

class Program
{
    static void PerformLogTest(Guid serviceTreeId, ITokenCredentialProvider tokenProvider)
    {
        Console.WriteLine($"Generate Test Log For Service Id '{serviceTreeId}'...");
        Console.WriteLine("Gathering Local User Info...");
        Console.WriteLine($"Appliction Id : '{tokenProvider.ApplicationId}'");
        Console.WriteLine($"Tenant Id : '{tokenProvider.TenantId}'");
        var audiLogger = new SecurityAuditLogger(serviceTreeId);
        Console.WriteLine($"Writing Test Audit Log...");
        audiLogger.LogSecretUpdate(tokenProvider, "TestSecret", "Key Vault", "Test Location");
        Console.WriteLine($"Test Log For Service Id '{serviceTreeId}' Complete...");
    }

    static void Main(string[] args)
    {
        var tokenProvider = new SecretManagerCredentialProvider();
        var serviceTreeId = Guid.Parse("8835b1f3-0d22-4e28-bae0-65da04655ed4");

        // If any arguments are provided, attempt to extract the service tree id from the arguments
        if (args.Length > 0)
        {
            var argsList = args.ToList();
            serviceTreeId = ExtractAndRemoveServiceTreeIdFromConsoleArguments(ref argsList);
        }
        
        PerformLogTest(serviceTreeId, tokenProvider);
    }

    internal static Guid ExtractAndRemoveServiceTreeIdFromConsoleArguments(ref List<string> args)
    {
        var result = Guid.Empty;
        const string serviceTreeIdArgName = "ServiceTreeId=";

        // Check for local enviroment values to indicate you are running for azure dev ops
        // SYSTEM_COLLECTIONURI is a default environment variable in azure dev ops
        bool isRunningInAzureDevOps = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SYSTEM_COLLECTIONURI"));

        // Iterate through the arguments and attempt to extract the service tree id
        for (int i = 0; i < args.Count; i++)
        {
            // Extract the value associated with the argument ServiceTreeId
            if (args[i].StartsWith(serviceTreeIdArgName, StringComparison.OrdinalIgnoreCase))
            {
                var value = args[i].Substring(serviceTreeIdArgName.Length);
                var serviceTreeIdReadSuccess = Guid.TryParse(value, out var serviceTreeId);
                if (serviceTreeIdReadSuccess)
                {
                    args.RemoveAt(i);
                    result = serviceTreeId;
                    break;
                }
                // If running in azure dev ops and the value for the argument 'ServiceTreeId' is not a valid Guid,
                // write a warning to console using AzDo warning comment pattern syntax.
                else if (isRunningInAzureDevOps)
                {
                    Console.WriteLine("##vso[task.logissue type=warning]Failed to parse a valid Guid value from ServiceTreeId value '{value}! Security Audit logging will be suppressed!");
                }
            }
        }

        // If running in azure dev ops and no service tree id is provided,
        // write a warning to console using AzDo warning comment pattern syntax.
        if (result == Guid.Empty && isRunningInAzureDevOps)
        {
            Console.WriteLine("##vso[task.logissue type=warning]ServiceTreeId is not provided! Security Audit logging will be suppressed!");
        }
        return result;
    }
}
