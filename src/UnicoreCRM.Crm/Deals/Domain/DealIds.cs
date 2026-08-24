namespace UnicoreCRM.Crm.Deals.Domain;

internal static class DealIds
{
    internal static string New(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
