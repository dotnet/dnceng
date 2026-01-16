using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("azure-managed-grafana-api-key")]
public class AzureManagedGrafanaApiKey : GenericAccessToken
{
    private readonly string[] _expirationDateFormats = new[] { "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss" };

    protected override string HelpMessage => "Please login to https://{0} and navigate to Administration > Service accounts to create a new service account token.";
    protected override string TokenName => "Azure Managed Grafana API key";
    protected override string TokenFormatDescription => "Service account token (starts with 'glsa_')";
    protected override string ExpirationFormatDescription => "format yyyy-MM-dd followed by optional time part hh:mm:ss or empty for no expiration";
    protected override bool HasExpiration => true;

    protected override IEnumerable<KeyValuePair<string, string>> EnvironmentToHost => new[]
    {
        new KeyValuePair<string, string>( "production", "https://dnceng-grafana-eraubnb4dkatgnfn.wus2.grafana.azure.com/" ),
        new KeyValuePair<string, string>( "staging", "https://dnceng-grafana-staging-faf3f3ebf0f8afbm.wus2.grafana.azure.com/" )
    };

    public AzureManagedGrafanaApiKey(ISystemClock clock, IConsole console) : base(clock, console)
    {
    }

    protected override bool TryParseExpirationDate(string value, out DateTime parsedValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            parsedValue = DateTime.MaxValue;
            return true;
        }
        return DateTime.TryParseExact(value, _expirationDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedValue);
    }

    protected override bool ValidateToken(string token)
    {
        // Azure Managed Grafana service account tokens start with "glsa_"
        if (token.StartsWith("glsa_", StringComparison.Ordinal))
        {
            Console.WriteLine("Azure Managed Grafana service account token validated successfully.");
            return true;
        }

        Console.WriteLine("Invalid token format. Azure Managed Grafana tokens must start with 'glsa_'.");
        return false;
    }
}
