using System;
using System.Text.RegularExpressions;

namespace Microsoft.DncEng.SecretManager.StorageTypes;

public class ServiceConnectionMagicString
{
    private static readonly string stringFormat = "Do not edit authentication. This is managed by secret-manager. Expires on {0}. Next rotation on {1}.";

    private static readonly Regex regex = new(@"Do not edit authentication\. This is managed by secret-manager\. Expires on (?<ExpirationDate>[0-9]{4}-[0-9]{1,2}-[0-9]{1,2})\. Next rotation on (?<NextRotationDate>[0-9]{4}-[0-9]{1,2}-[0-9]{1,2})\.", RegexOptions.None, TimeSpan.FromSeconds(10));

    public static string CreateMagicString(DateOnly expirationDate, DateOnly nextRotationDate)
    {
        return string.Format(stringFormat, expirationDate.ToString("yyyy-MM-dd"), nextRotationDate.ToString("yyyy-MM-dd"));
    }

    public static (DateOnly ExpirationDate, DateOnly NextRotationDate)? ParseMagicString(string magicString)
    {
        Match m = regex.Match(magicString);

        if (!m.Success)
            return null;

        if (!DateOnly.TryParseExact(m.Groups["ExpirationDate"].Value, "yyyy-M-d", out DateOnly expirationDate))
        {
            return null;
        }

        if (!DateOnly.TryParseExact(m.Groups["NextRotationDate"].Value, "yyyy-M-d", out DateOnly nextRotationDate))
        {
            return null;
        }

        return (expirationDate, nextRotationDate);
    }
}