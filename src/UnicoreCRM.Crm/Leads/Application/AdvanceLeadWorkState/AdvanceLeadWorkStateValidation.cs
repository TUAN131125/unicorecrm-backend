using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Leads.Domain;

namespace UnicoreCRM.Crm.Leads.Application.AdvanceLeadWorkState;

internal static class AdvanceLeadWorkStateValidation
{
    internal static bool TryAdvance(
        AdvanceLeadWorkStateRequest request,
        out LeadWorkState target,
        out LeadVerificationProfile verification,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        target = request.TargetWorkState switch
        {
            "CONTACTING" => LeadWorkState.Contacting,
            "VERIFYING" => LeadWorkState.Verifying,
            _ => InvalidTarget(fields)
        };
        if (target != LeadWorkState.Verifying && request.VerificationProfile is not null)
            fields["verificationProfile"] = ["verificationProfile is accepted only when targetWorkState is VERIFYING."];
        verification = new LeadVerificationProfile(
            LeadValidation.Text(request.VerificationProfile?.CompanyName, "verificationProfile.companyName", 0, 240, false, fields),
            LeadValidation.Text(request.VerificationProfile?.PainPoint, "verificationProfile.painPoint", 0, 4000, false, fields),
            LeadValidation.Utc(request.VerificationProfile?.NextFollowUpAt, "verificationProfile.nextFollowUpAt", false, fields));
        errors = fields;
        return fields.Count == 0;
    }

    /// <summary>
    /// Profile completeness required before a Lead may leave its current work state.
    /// </summary>
    internal static IReadOnlyDictionary<string, string[]> ProgressiveProfileErrors(LeadProfile profile)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (profile.DisplayName.Length == 0)
            fields["displayName"] = ["displayName is required for this Lead state."];
        if (profile.OwnerId.Length == 0)
            fields["ownerId"] = ["ownerId is required for this Lead state."];
        if (!new[] { profile.Phone, profile.WorkPhone, profile.OtherPhone, profile.Email, profile.PersonalEmail, profile.ZaloId, profile.Facebook }
            .Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            fields["contactChannel"] = ["At least one Lead contact channel is required for this Lead state."];
        }
        return fields;
    }

    private static LeadWorkState InvalidTarget(IDictionary<string, string[]> fields)
    {
        fields["targetWorkState"] = ["targetWorkState must be CONTACTING or VERIFYING."];
        return LeadWorkState.New;
    }
}
