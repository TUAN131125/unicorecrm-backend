using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Leads.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.Common;

/// <summary>
/// The authorization result every Leads use case works from: the trusted Workspace plus the
/// AccessControl decision that governs this resource for this caller.
/// </summary>
internal sealed record LeadAccess(TrustedWorkspaceContext Trusted, RecordAccessAuthorization Authorization);

/// <summary>
/// Leads-side enforcement of the AccessControl field-security decision. Leads decides nothing here:
/// AccessControl has already reduced the policy to a per-field <see cref="RecordFieldEnforcement"/>,
/// and this type only applies it to the Leads wire vocabulary. The representation rules are the ones
/// frozen for Support: a withheld optional field is omitted, a withheld required field fails the
/// operation closed, MASKED is enforced as withheld, and READ_ONLY blocks writes.
/// </summary>
internal static class LeadFieldSecurity
{
    /// <summary>
    /// The field keys Leads can enforce a policy on, mapped to whether the wire contract makes the
    /// field required. These are the <c>LeadDocument</c> property names, generated from that record
    /// so the vocabulary cannot drift from what Leads actually projects.
    ///
    /// <para>The wire schema declares further properties (<c>notes</c>, <c>relationshipRef</c>,
    /// <c>dealRef</c>, <c>qualifiedDealId</c>, <c>archivedAt</c> and the merge/consent family) that
    /// Leads does not project at all. A policy naming one of them cannot be enforced and fails the
    /// operation closed rather than being silently ignored.</para>
    /// </summary>
    internal static IReadOnlyDictionary<string, bool> EnforceableFields { get; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = true,
            ["displayName"] = true,
            ["estimatedValue"] = true,
            ["version"] = true,
            ["createdAt"] = true,
            ["updatedAt"] = true,
            ["leadWorkState"] = true,
            ["source"] = true,
            ["score"] = true,
            ["ownerId"] = true,
            ["interestedProducts"] = true,
            ["activityProjection"] = true,
            ["title"] = false,
            ["companyName"] = false,
            ["email"] = false,
            ["phone"] = false,
            ["qualificationOutcome"] = false,
            ["nextFollowUpAt"] = false,
            ["priority"] = false,
            ["tags"] = false,
            ["companySize"] = false,
            ["industry"] = false,
            ["salutation"] = false,
            ["department"] = false,
            ["workPhone"] = false,
            ["otherPhone"] = false,
            ["personalEmail"] = false,
            ["zaloId"] = false,
            ["facebook"] = false,
            ["preferredChannel"] = false,
            ["doNotCall"] = false,
            ["doNotEmail"] = false,
            ["businessType"] = false,
            ["website"] = false,
            ["taxCode"] = false,
            ["companyAddress"] = false,
            ["country"] = false,
            ["province"] = false,
            ["district"] = false,
            ["ward"] = false,
            ["contactAddress"] = false,
            ["campaignId"] = false,
            ["assignedTeam"] = false,
            ["decisionRole"] = false,
            ["budgetRange"] = false,
            ["purchaseTimeline"] = false,
            ["painPoint"] = false,
            ["followUpNote"] = false,
            ["description"] = false,
            ["internalNotes"] = false,
            ["customFields"] = false,
            ["disqualifiedAt"] = false,
            ["disqualifiedBy"] = false,
            ["disqualificationReason"] = false,
            ["disqualificationNote"] = false
        };

    internal static IReadOnlyList<string> FieldKeys { get; } = EnforceableFields.Keys.Order(StringComparer.Ordinal).ToArray();

    internal static LeadDocument Project(LeadDocument model, RecordAccessAuthorization access) =>
        model with
        {
            Title = access.CanRead("title") ? model.Title : null,
            CompanyName = access.CanRead("companyName") ? model.CompanyName : null,
            Email = access.CanRead("email") ? model.Email : null,
            Phone = access.CanRead("phone") ? model.Phone : null,
            QualificationOutcome = access.CanRead("qualificationOutcome") ? model.QualificationOutcome : null,
            NextFollowUpAt = access.CanRead("nextFollowUpAt") ? model.NextFollowUpAt : null,
            Priority = access.CanRead("priority") ? model.Priority : null,
            Tags = access.CanRead("tags") ? model.Tags : null,
            CompanySize = access.CanRead("companySize") ? model.CompanySize : null,
            Industry = access.CanRead("industry") ? model.Industry : null,
            Salutation = access.CanRead("salutation") ? model.Salutation : null,
            Department = access.CanRead("department") ? model.Department : null,
            WorkPhone = access.CanRead("workPhone") ? model.WorkPhone : null,
            OtherPhone = access.CanRead("otherPhone") ? model.OtherPhone : null,
            PersonalEmail = access.CanRead("personalEmail") ? model.PersonalEmail : null,
            ZaloId = access.CanRead("zaloId") ? model.ZaloId : null,
            Facebook = access.CanRead("facebook") ? model.Facebook : null,
            PreferredChannel = access.CanRead("preferredChannel") ? model.PreferredChannel : null,
            DoNotCall = access.CanRead("doNotCall") ? model.DoNotCall : null,
            DoNotEmail = access.CanRead("doNotEmail") ? model.DoNotEmail : null,
            BusinessType = access.CanRead("businessType") ? model.BusinessType : null,
            Website = access.CanRead("website") ? model.Website : null,
            TaxCode = access.CanRead("taxCode") ? model.TaxCode : null,
            CompanyAddress = access.CanRead("companyAddress") ? model.CompanyAddress : null,
            Country = access.CanRead("country") ? model.Country : null,
            Province = access.CanRead("province") ? model.Province : null,
            District = access.CanRead("district") ? model.District : null,
            Ward = access.CanRead("ward") ? model.Ward : null,
            ContactAddress = access.CanRead("contactAddress") ? model.ContactAddress : null,
            CampaignId = access.CanRead("campaignId") ? model.CampaignId : null,
            AssignedTeam = access.CanRead("assignedTeam") ? model.AssignedTeam : null,
            DecisionRole = access.CanRead("decisionRole") ? model.DecisionRole : null,
            BudgetRange = access.CanRead("budgetRange") ? model.BudgetRange : null,
            PurchaseTimeline = access.CanRead("purchaseTimeline") ? model.PurchaseTimeline : null,
            PainPoint = access.CanRead("painPoint") ? model.PainPoint : null,
            FollowUpNote = access.CanRead("followUpNote") ? model.FollowUpNote : null,
            Description = access.CanRead("description") ? model.Description : null,
            InternalNotes = access.CanRead("internalNotes") ? model.InternalNotes : null,
            CustomFields = access.CanRead("customFields") ? model.CustomFields : null,
            DisqualifiedAt = access.CanRead("disqualifiedAt") ? model.DisqualifiedAt : null,
            DisqualifiedBy = access.CanRead("disqualifiedBy") ? model.DisqualifiedBy : null,
            DisqualificationReason = access.CanRead("disqualificationReason") ? model.DisqualificationReason : null,
            DisqualificationNote = access.CanRead("disqualificationNote") ? model.DisqualificationNote : null
        };

    internal static LeadOperationError? UnenforceablePolicy(RecordAccessAuthorization access) =>
        access.UnenforceableFieldKeys.Count == 0
            ? null
            : new LeadOperationError(
                "ACCESS_DENIED",
                403,
                "Access denied",
                "A field-security policy applies to a field this resource cannot withhold, so the request is refused rather than returning a value the policy forbids.");

    internal static LeadOperationError? GuardFieldWrite(RecordAccessAuthorization access, params string[] fieldKeys)
    {
        var blocked = fieldKeys.Where(fieldKey => !access.CanWrite(fieldKey)).Order(StringComparer.Ordinal).ToArray();
        return blocked.Length == 0
            ? null
            : new LeadOperationError(
                "ACCESS_DENIED",
                403,
                "Access denied",
                $"Field security does not permit writing: {string.Join(", ", blocked)}.");
    }
}

/// <summary>
/// The Leads application boundary of the trusted authority chain: authenticated user -> requested
/// Workspace -> verified membership -> trusted CurrentWorkspace -> capability authorization ->
/// record scope -> field security -> Leads use case.
///
/// <para>Everything beyond the capability check is decided by AccessControl through
/// <see cref="IRecordAccessEvaluator"/>. Leads holds no scope rule and no field rule of its own.</para>
/// </summary>
internal sealed class LeadAuthorization(IRecordAccessEvaluator evaluator)
{
    internal const string ResourceKey = "leads";

    internal async Task<LeadOperationResult<LeadAccess>> AuthorizeAsync(
        AccessRequirement requirement,
        LeadRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var authorization = await evaluator.AuthorizeResourceAsync(
            ResourceKey,
            requirement.Capability,
            LeadFieldSecurity.FieldKeys,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);

        if (authorization.TrustedWorkspace is not { } trusted)
        {
            return LeadOperationResult<LeadAccess>.Failure(
                authorization.Code == "WORKSPACE_MISMATCH" ? LeadErrors.WorkspaceMismatch() : LeadErrors.AccessDenied());
        }

        if (!authorization.IsAllowed)
            return LeadOperationResult<LeadAccess>.Failure(LeadErrors.AccessDenied());

        var unenforceable = LeadFieldSecurity.UnenforceablePolicy(authorization);
        if (unenforceable is not null)
            return LeadOperationResult<LeadAccess>.Failure(unenforceable);

        return LeadOperationResult<LeadAccess>.Success(new LeadAccess(trusted, authorization));
    }

    /// <summary>
    /// Enforces record scope against the Leads-owned authoritative fact. <c>LeadProfile.OwnerId</c>
    /// is the member reference Leads records for a lead and is validated on write through the narrow
    /// Workspace active-member contract; nothing else in the aggregate is a member owner, so nothing
    /// else is substituted for one. A lead outside scope is reported as not found.
    /// </summary>
    /// <param name="writtenFieldKeys">
    /// The wire fields the command would change. They are checked only after record scope allows the
    /// record, so a hidden lead is reported as missing rather than leaking a field-policy refusal.
    /// </param>
    internal async Task<LeadOperationError?> EnforceRecordAsync(
        LeadAccess access,
        Lead lead,
        string enforcementPoint,
        LeadRequestMetadata metadata,
        CancellationToken cancellationToken,
        params string[] writtenFieldKeys)
    {
        var decision = await evaluator.AuthorizeRecordAsync(
            access.Authorization,
            lead.LeadId,
            RecordAccessFacts.Found(lead.Profile.OwnerId),
            enforcementPoint,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);
        if (!decision.IsAllowed)
            return LeadErrors.NotFound();
        return writtenFieldKeys.Length == 0
            ? null
            : LeadFieldSecurity.GuardFieldWrite(access.Authorization, writtenFieldKeys);
    }
}
