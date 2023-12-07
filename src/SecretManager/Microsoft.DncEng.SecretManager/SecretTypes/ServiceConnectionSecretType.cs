using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.VisualStudio.Services.Security;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("service-connection")]
public class ServiceConnectionSecretType : SecretType<ServiceConnectionSecretType.Parameters>
{
    private readonly IConsole _console;

    private const string _helpMessage = """
        Service Connections have no standard way to rotate their tokens. Please follow instructions 
        in the DNCEng Internal Wiki at:
        
        > https://dev.azure.com/dnceng/internal/_wiki/wikis/DNCEng%20Services%20Wiki/1157/Generic-Service-Connections.

        Direct link to this service connection in the portal: 
        
        > https://dev.azure.com/{0}/{1}/_settings/adminservices?resourceId={2}

        """;

    public class Parameters
    {
        public string Description { get; set; }
        public string Organization { get; set; }
        public string Project { get; set; }
        public Guid Id { get; set; }
        public string Name { get; set; }
    }

    public ServiceConnectionSecretType(IConsole console)
    {
        _console = console;
    }

    protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        if (!_console.IsInteractive)
        {
            throw new HumanInterventionRequiredException("User intervention required for creation or rotation of a service connection token.");
        }

        _console.WriteLine(string.Format(_helpMessage, parameters.Organization, parameters.Project, parameters.Id));

        DateTime expiresOn = await _console.PromptAndValidateAsync($"Secret expiration in the form yyyy-MM-dd",
                $"Secret expiration format must be \"yyyy-MM-dd\".",
                (ConsoleExtension.TryParse<DateTime>)TryParseExpirationDate);

        DateTime nextRotateOn = expiresOn.AddDays(-15);

        return new SecretData(string.Empty, expiresOn, nextRotateOn);
    }
    
    protected bool TryParseExpirationDate(string value, out DateTime parsedValue)
    {
        return DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedValue);
    }

}