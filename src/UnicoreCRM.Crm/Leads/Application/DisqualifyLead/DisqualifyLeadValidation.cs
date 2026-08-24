using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.DisqualifyLead;

internal static class DisqualifyLeadValidation
{
    internal static bool TryDisqualify(
        DisqualifyLeadRequest request,
        out string? reason,
        out string? evidence,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        reason = LeadValidation.Text(request.Reason, "reason", 1, 1000, true, fields);
        evidence = LeadValidation.Text(request.Evidence, "evidence", 1, 4000, true, fields);
        errors = fields;
        return fields.Count == 0;
    }
}
