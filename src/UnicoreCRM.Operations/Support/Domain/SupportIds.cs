namespace UnicoreCRM.Operations.Support.Domain;

/// <summary>
/// Support owns and generates every Support aggregate and evidence identifier. No caller,
/// foreign module or workflow may supply a SupportCase identity.
/// </summary>
internal static class SupportIds
{
    internal static string New(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
