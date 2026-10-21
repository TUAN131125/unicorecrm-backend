using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using UnicoreCRM.Crm.Leads.Domain;

namespace UnicoreCRM.Crm.Leads.Application.ListLeads;

internal sealed class LeadTableCursor(IDataProtectionProvider protection, string binding)
{
    private readonly IDataProtector protector = protection.CreateProtector("Leads.ListCursor.v1", binding);

    internal string Encode(Lead lead) => protector.Protect(LeadListCursor.Encode(lead));

    internal bool TryParse(string? cursor, IDictionary<string, string[]> fields,
        out DateTimeOffset? updatedAt, out string? leadId)
    {
        updatedAt = null;
        leadId = null;
        if (cursor is null) return true;
        try
        {
            if (cursor.Length is 0 or > 4096) throw new FormatException();
            return LeadListCursor.TryParse(protector.Unprotect(cursor), fields, out updatedAt, out leadId);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or ArgumentException)
        {
            fields["cursor"] = ["cursor is invalid for this Lead list query."];
            return false;
        }
    }
}
