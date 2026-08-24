using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Workflows.Durable.Application.Common;
using UnicoreCRM.Workflows.Durable.Contracts;

namespace UnicoreCRM.Workflows.Durable.Application.ProvisionInitialWorkspace;

/// <summary>
/// The server-owned, deterministic and documented defaults for Initial Workspace Provisioning.
/// The explicit Skip path is exactly the request that omits every optional value, so Skip and
/// Finish reach the same canonical business intent through the same code path.
/// </summary>
internal static partial class ProvisioningDefaults
{
    internal const string Name = "My Workspace";
    internal const string Locale = "en";
    internal const string TimeZone = "UTC";
    internal const string BaseCurrency = "USD";
    internal const string FallbackLogoText = "W";

    /// <summary>The implemented CRM owners. Module enablement is not caller-selectable.</summary>
    internal static IReadOnlyList<string> EnabledModuleKeys { get; } = ["leads", "deals", "tasks"];

    /// <summary>Studio and People remain deferred surfaces, so only the CRM product space is enabled.</summary>
    internal static IReadOnlyList<string> AvailableProductSpaces { get; } = ["crm"];

    private static readonly string[] SupportedLocales = [Locale, "vi"];

    internal static DurableWorkflowError? Resolve(
        ProvisionInitialWorkspaceRequest request,
        out string name,
        out string logoText,
        out InitialWorkspaceConfigurationSeed configuration)
    {
        name = Name;
        logoText = FallbackLogoText;
        configuration = new InitialWorkspaceConfigurationSeed(Locale, TimeZone, BaseCurrency, EnabledModuleKeys, AvailableProductSpaces);
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var resolvedName = Trimmed(request.Name) ?? Name;
        if (resolvedName.Length is < 1 or > 200)
            fields["name"] = ["name must contain between 1 and 200 characters."];

        var suppliedLogo = Trimmed(request.LogoText);
        if (suppliedLogo is not null && suppliedLogo.Length is < 1 or > 8)
            fields["logoText"] = ["logoText must contain between 1 and 8 characters."];

        var resolvedLocale = Trimmed(request.Locale) ?? Locale;
        if (!SupportedLocales.Contains(resolvedLocale, StringComparer.Ordinal))
            fields["locale"] = ["locale must be one of: en, vi."];

        var resolvedTimeZone = Trimmed(request.TimeZone) ?? TimeZone;
        if (resolvedTimeZone.Length is < 1 or > 100)
            fields["timeZone"] = ["timeZone must contain between 1 and 100 characters."];

        var resolvedCurrency = Trimmed(request.BaseCurrency) ?? BaseCurrency;
        if (!CurrencyPattern().IsMatch(resolvedCurrency))
            fields["baseCurrency"] = ["baseCurrency must be a three-letter uppercase currency code."];

        if (fields.Count != 0)
            return DurableWorkflowErrors.Validation(fields);

        name = resolvedName;
        logoText = suppliedLogo ?? ComposeLogoText(resolvedName);
        configuration = new InitialWorkspaceConfigurationSeed(
            resolvedLocale,
            resolvedTimeZone,
            resolvedCurrency,
            EnabledModuleKeys,
            AvailableProductSpaces);
        return null;
    }

    /// <summary>Derives the default logo text from the resolved name so Skip stays deterministic.</summary>
    internal static string ComposeLogoText(string name)
    {
        var initials = name
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => word.Where(char.IsLetterOrDigit).Take(1).ToArray())
            .Where(letters => letters.Length != 0)
            .Take(2)
            .Select(letters => char.ToUpperInvariant(letters[0]))
            .ToArray();
        return initials.Length == 0 ? FallbackLogoText : new string(initials);
    }

    /// <summary>Hashes the effective provisioning values so a reused key with changed values fails closed.</summary>
    internal static string Fingerprint(string name, string logoText, InitialWorkspaceConfigurationSeed configuration)
    {
        var canonical = string.Join(
            '\n',
            name,
            logoText,
            configuration.Locale,
            configuration.TimeZone,
            configuration.BaseCurrency,
            string.Join(',', configuration.EnabledModuleKeys),
            string.Join(',', configuration.AvailableProductSpaces));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyPattern();
}
