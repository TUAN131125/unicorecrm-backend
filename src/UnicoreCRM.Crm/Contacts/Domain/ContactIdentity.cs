namespace UnicoreCRM.Crm.Contacts.Domain;

internal static class ContactIds
{
    internal static string New(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}

/// <summary>
/// The frozen Contact email identity rule (`DEC-LEAD-CONTACT-DUPLICATE-POLICY` section 9.2). It is
/// deliberately the same normalization IdentityAuth already uses as its account uniqueness and
/// lookup key - <c>Trim().ToUpperInvariant()</c> over the whole address, local part included.
/// Adopting a second, different email-equality rule inside one system would be the invention.
///
/// Nothing else is applied: no plus-address stripping, no dot removal, no IDN or punycode folding,
/// no alias expansion, no locale-sensitive casing. Each of those needs provider-specific knowledge
/// that no authority supplies, and each would over-match distinct people.
///
/// An absent, empty or whitespace-only address yields no key. It is never normalized to an empty
/// string, so two Contacts without an email never match each other.
/// </summary>
internal static class ContactEmailIdentity
{
    internal static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}
