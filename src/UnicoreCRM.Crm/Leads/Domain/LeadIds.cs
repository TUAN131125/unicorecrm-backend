namespace UnicoreCRM.Crm.Leads.Domain;

internal static class LeadIds
{
    internal static string New(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
