using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("helix-access-token")]
public class HelixAccessToken : GenericAccessToken
{
    protected override string HelpMessage => "Please login to https://{0}/Account/Tokens using your GitHub account and create a new token.";
    protected override string TokenName => "Helix Access Token";
    protected override string TokenFormatDescription => "in the form dnp2.<nonnegative Int32 token ID>.<84-character Helix HIS-v2 secret>, or legacy base64url with at least 24 characters";
    protected override bool HasExpiration => false;

    protected override IEnumerable<KeyValuePair<string, string>> EnvironmentToHost => new[]
    {
        new KeyValuePair<string, string>( "production", "helix.dot.net" ),
        new KeyValuePair<string, string>( "staging", "helix.int-dot.net" )
    };

    protected override string ExpirationFormatDescription => throw new NotImplementedException();

    public HelixAccessToken(ISystemClock clock, IConsole console) : base(clock, console)
    {
    }

    protected override bool ValidateToken(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        if (Regex.IsMatch(token, @"\A[a-zA-Z0-9_\-]{24,}\z"))
        {
            return true;
        }

        // Short-form HIS-v2 keys have Base62 data, allocation metadata, and the Helix provider signature.
        Match match = Regex.Match(token,
            @"\Adnp2\.(?<tokenId>[0-9]+)\.[a-zA-Z0-9]{52}JQQJ99[a-zA-Z0-9][A-L][a-zA-Z0-9]{16}dnhp[a-zA-Z0-9]{4}\z");
        return match.Success && int.TryParse(match.Groups["tokenId"].Value,
            NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    protected override bool TryParseExpirationDate(string value, out DateTime parsedValue)
    {
        throw new NotImplementedException();
    }
}
