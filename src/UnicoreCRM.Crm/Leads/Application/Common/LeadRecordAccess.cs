using System.Globalization;
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
    /// <para><c>relationshipRef</c> and <c>dealRef</c> are projected because positive qualification
    /// writes them. The wire schema still declares further properties (<c>notes</c>,
    /// <c>qualifiedDealId</c>, <c>archivedAt</c> and the merge/consent family) that
    /// Leads does not project at all. A policy naming one of them fails closed as an unknown key.</para>
    ///
    /// <para>Two rules, frozen and distinct. A policy naming a key <b>outside</b> this vocabulary is
    /// not readable and not writable - the key fails closed and the public evaluation reports it
    /// HIDDEN - and does not by itself refuse the operation, because this owner never projects it.
    /// A policy naming a key <b>inside</b> this vocabulary that the representation being returned
    /// makes required cannot be honoured at all, and refuses the operation rather than returning a
    /// value the policy forbids.</para>
    /// </summary>
    internal static IReadOnlyDictionary<string, bool> EnforceableFields { get; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = true,
            ["displayName"] = true,
            ["estimatedValue"] = false,
            ["version"] = true,
            ["createdAt"] = true,
            ["updatedAt"] = true,
            ["leadWorkState"] = true,
            ["source"] = false,
            ["score"] = true,
            ["ownerId"] = true,
            ["interestedProducts"] = true,
            ["activityProjection"] = true,
            ["title"] = false,
            ["companyName"] = false,
            ["email"] = false,
            ["phone"] = false,
            ["qualificationOutcome"] = false,
            ["relationshipRef"] = false,
            ["dealRef"] = false,
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
            EstimatedValue = access.CanRead("estimatedValue") ? model.EstimatedValue : null,
            Source = access.CanRead("source") ? model.Source : null,
            Title = access.CanRead("title") ? model.Title : null,
            CompanyName = access.CanRead("companyName") ? model.CompanyName : null,
            Email = access.CanRead("email") ? model.Email : null,
            Phone = access.CanRead("phone") ? model.Phone : null,
            QualificationOutcome = access.CanRead("qualificationOutcome") ? model.QualificationOutcome : null,
            RelationshipRef = access.CanRead("relationshipRef") ? model.RelationshipRef : null,
            DealRef = access.CanRead("dealRef") ? model.DealRef : null,
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

    /// <summary>
    /// The refusal a caller receives when a restrictive policy names a field the representation being
    /// returned cannot omit. AccessControl decides that, against the representation the operation
    /// declared; this owner only applies the answer.
    /// </summary>
    internal static LeadOperationError? UnenforceablePolicy(RecordAccessAuthorization access) =>
        access.UnenforceableFieldKeys.Count == 0
            ? null
            : new LeadOperationError(
                "ACCESS_DENIED",
                403,
                "Access denied",
                "A field-security policy applies to a field this resource cannot withhold, so the request is refused rather than returning a value the policy forbids.");

    internal static LeadOperationError? GuardFieldWrite(RecordAccessAuthorization access, params string[] fieldKeys) =>
        Refusal(fieldKeys.Where(fieldKey => !access.CanWrite(fieldKey)).ToList());

    /// <summary>
    /// Refuses a profile replacement that would change a field the caller may not write. The check
    /// compares the requested profile against the stored aggregate, so replacing a field with the
    /// value it already holds is not a write and is not refused. Without this comparison a full
    /// profile replacement would either send every profile field through the write check - refusing
    /// unchanged READ_ONLY values - or send none, which is what let a READ_ONLY field be replaced.
    /// </summary>
    internal static LeadOperationError? GuardProfileWrite(
        RecordAccessAuthorization access,
        LeadProfile current,
        LeadProfile requested)
    {
        var currentValues = Values(current);
        var requestedValues = Values(requested);
        var blocked = new List<string>();
        foreach (var pair in requestedValues)
        {
            if (!access.CanWrite(pair.Key) && !string.Equals(currentValues[pair.Key], pair.Value, StringComparison.Ordinal))
                blocked.Add(pair.Key);
        }
        return Refusal(blocked);
    }

    /// <summary>
    /// Refuses a creation that populates a field the caller may not write. Creation has no stored
    /// value to compare against, so every field the request actually sets counts as a write, and the
    /// fields the create contract makes mandatory always count.
    /// </summary>
    internal static LeadOperationError? GuardCreateWrite(RecordAccessAuthorization access, LeadProfile profile)
    {
        var values = Values(profile);
        var blocked = new List<string>();
        foreach (var pair in values)
        {
            var written = RequiredCreateFields.Contains(pair.Key, StringComparer.Ordinal) || pair.Value.Length != 0;
            if (written && !access.CanWrite(pair.Key))
                blocked.Add(pair.Key);
        }
        return Refusal(blocked);
    }

    /// <summary>
    /// The create-contract fields a Lead always carries a value for. A non-writable required create
    /// field fails the creation closed: there is no admitted representation of a Lead created
    /// without a display name or owner.
    /// </summary>
    private static readonly string[] RequiredCreateFields =
        ["displayName", "ownerId"];

    /// <summary>
    /// The profile as its wire field vocabulary, each value reduced to a canonical string so a
    /// change is decided by value and not by object identity. An empty string means the profile
    /// carries no value for that field.
    /// </summary>
    private static Dictionary<string, string> Values(LeadProfile profile) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["displayName"] = Text(profile.DisplayName),
            ["salutation"] = Text(profile.Salutation),
            ["title"] = Text(profile.Title),
            ["department"] = Text(profile.Department),
            ["phone"] = Text(profile.Phone),
            ["workPhone"] = Text(profile.WorkPhone),
            ["otherPhone"] = Text(profile.OtherPhone),
            ["email"] = Text(profile.Email),
            ["personalEmail"] = Text(profile.PersonalEmail),
            ["zaloId"] = Text(profile.ZaloId),
            ["facebook"] = Text(profile.Facebook),
            ["preferredChannel"] = Text(profile.PreferredChannel),
            ["doNotCall"] = Text(profile.DoNotCall),
            ["doNotEmail"] = Text(profile.DoNotEmail),
            ["companyName"] = Text(profile.CompanyName),
            ["companySize"] = Text(profile.CompanySize),
            ["industry"] = Text(profile.Industry),
            ["businessType"] = Text(profile.BusinessType),
            ["website"] = Text(profile.Website),
            ["taxCode"] = Text(profile.TaxCode),
            ["companyAddress"] = Text(profile.CompanyAddress),
            ["country"] = Text(profile.Country),
            ["province"] = Text(profile.Province),
            ["district"] = Text(profile.District),
            ["ward"] = Text(profile.Ward),
            ["contactAddress"] = Text(profile.ContactAddress),
            ["source"] = Text(profile.Source),
            ["campaignId"] = Text(profile.CampaignId),
            ["ownerId"] = Text(profile.OwnerId),
            ["assignedTeam"] = Text(profile.AssignedTeam),
            ["decisionRole"] = Text(profile.DecisionRole),
            ["priority"] = Text(profile.Priority),
            ["interestedProducts"] = string.Join("\u001f", profile.InterestedProducts.Select(item =>
                string.Join("\u001e", item.ProductId, item.ProductNameSnapshot, item.InterestLevel, Text(item.EstimatedQuantity), Money(item.ExpectedBudget), Text(item.Note)))),
            ["estimatedValue"] = Money(profile.EstimatedValue),
            ["budgetRange"] = Text(profile.BudgetRange),
            ["purchaseTimeline"] = Text(profile.PurchaseTimeline),
            ["painPoint"] = Text(profile.PainPoint),
            ["nextFollowUpAt"] = Text(profile.NextFollowUpAt),
            ["followUpNote"] = Text(profile.FollowUpNote),
            ["tags"] = string.Join("\u001f", profile.Tags),
            ["description"] = Text(profile.Description),
            ["internalNotes"] = Text(profile.InternalNotes),
            ["customFields"] = string.Join("\u001f", profile.CustomFields.Select(item =>
                string.Join("\u001e", item.FieldKey, item.ValueType, Text(item.StringValue), Text(item.DecimalValue), Text(item.BooleanValue), string.Join(",", item.StringArrayValue ?? []))))
        };

    private static string Text(string? value) => value ?? string.Empty;
    private static string Text(bool? value) => value is null ? string.Empty : value.Value ? "true" : "false";
    private static string Text(int? value) => value is null ? string.Empty : value.Value.ToString(CultureInfo.InvariantCulture);
    private static string Text(DateTimeOffset? value) => value is null ? string.Empty : value.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    private static string Money(LeadMoney? value) => value is null ? string.Empty : $"{value.Amount}|{value.Currency}";

    private static LeadOperationError? Refusal(List<string> blocked) =>
        blocked.Count == 0
            ? null
            : new LeadOperationError(
                "ACCESS_DENIED",
                403,
                "Access denied",
                $"Field security does not permit writing: {string.Join(", ", blocked.Order(StringComparer.Ordinal))}.");
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

    /// <param name="representation">
    /// The representation the calling operation will return. It decides only whether a restrictive
    /// policy on a field this resource declares required can be honoured by omitting the value; it
    /// can never widen read or write access. Operations returning the full read model pass
    /// <see cref="RecordAccessRepresentation.Full"/>, which is the default.
    /// </param>
    internal async Task<LeadOperationResult<LeadAccess>> AuthorizeAsync(
        AccessRequirement requirement,
        LeadRequestMetadata metadata,
        CancellationToken cancellationToken,
        RecordAccessRepresentation? representation = null)
    {
        var authorization = await evaluator.AuthorizeResourceAsync(
            ResourceKey,
            requirement.Capability,
            LeadFieldSecurity.FieldKeys,
            representation ?? RecordAccessRepresentation.Full,
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
    internal async Task<LeadOperationError?> EnforceRecordAsync(
        LeadAccess access,
        Lead lead,
        string enforcementPoint,
        LeadRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var decision = await evaluator.AuthorizeRecordAsync(
            access.Authorization,
            lead.LeadId,
            RecordAccessFacts.Found(lead.Profile.OwnerId),
            enforcementPoint,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);
        return decision.IsAllowed ? null : LeadErrors.NotFound();
    }

    /// <summary>
    /// Authorizes the fields a command is about to write. It is deliberately separate from the
    /// record guard and is applied only on the new-execution path: record scope is current
    /// authorization and must gate a replay, whereas a replay performs no write at all and must not
    /// be refused for lacking permission to write what was already written.
    /// </summary>
    internal static LeadOperationError? EnforceFieldWrite(LeadAccess access, params string[] writtenFieldKeys) =>
        writtenFieldKeys.Length == 0
            ? null
            : LeadFieldSecurity.GuardFieldWrite(access.Authorization, writtenFieldKeys);
}
