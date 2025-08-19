using Microsoft.DncEng.CommandLineLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("dotnetstatusweb-access-token")]
public class DotNetStatusWebAccessToken : GenericAccessToken
{
    public DotNetStatusWebAccessToken(ISystemClock clock, IConsole console) : base(clock, console)
    {
    }

    private readonly string[] _expirationDateFormats = new[] { "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss" };

    protected override bool HasExpiration => true;

    protected override string HelpMessage => "Please login to https://{0}/Token using your GitHub account and create a new token.";

    protected override string TokenName => "DotNet Status Web Access Token";

    protected override string TokenFormatDescription => "base64 url encoded with at least 24 characters";

    protected override IEnumerable<KeyValuePair<string, string>> EnvironmentToHost => new[]
    {
        new KeyValuePair<string, string>( "production", "dotneteng-status.azurewebsites.net" )
    };

    protected override bool TryParseExpirationDate(string value, out DateTime parsedValue)
    {
        return DateTime.TryParseExact(value, _expirationDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedValue);
    }

    protected override bool ValidateToken(string token)
    {
        return Regex.IsMatch(token, "^[a-zA-Z0-9_\\-]{24,}$");
    }
}