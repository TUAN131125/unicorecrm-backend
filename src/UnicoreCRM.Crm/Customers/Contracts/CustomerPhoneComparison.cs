using System.Text.RegularExpressions;

namespace UnicoreCRM.Crm.Customers.Contracts;

/// <summary>Exact full-number comparison; not an identity key or carrier allocation check.</summary>
public static partial class CustomerPhoneComparison
{
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var text = Separators().Replace(input.Trim(), "");
        var national = text.StartsWith("+84", StringComparison.Ordinal) ? "0" + text[3..]
            : text.StartsWith("84", StringComparison.Ordinal) ? "0" + text[2..] : text;
        if (VietnamNational().IsMatch(national)) return "+84" + national[1..];
        // Explicit international numbers retain their complete country code; no suffix matching.
        return !text.StartsWith("+84", StringComparison.Ordinal) && International().IsMatch(text) ? text : null;
    }

    public static IReadOnlyList<string> ExactStoredForms(string normalized) => normalized.StartsWith("+84", StringComparison.Ordinal)
        ? [normalized, normalized[1..], "0" + normalized[3..]] : [normalized];

    [GeneratedRegex(@"[ \t().-]", RegexOptions.CultureInvariant)] private static partial Regex Separators();
    [GeneratedRegex(@"^0(?:[35789][0-9]{8}|2[0-9]{9})$", RegexOptions.CultureInvariant)] private static partial Regex VietnamNational();
    [GeneratedRegex(@"^\+[1-9][0-9]{7,14}$", RegexOptions.CultureInvariant)] private static partial Regex International();
}
