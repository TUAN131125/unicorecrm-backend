using System.Globalization;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Deals.Application.Common;

/// <summary>
/// The authorization result every Deals use case works from: the trusted Workspace plus the
/// AccessControl decision that governs this resource for this caller.
/// </summary>
internal sealed record DealAccess(TrustedWorkspaceContext Trusted, RecordAccessAuthorization Authorization);

/// <summary>
/// Deals-side enforcement of the AccessControl field-security decision. Deals decides nothing
/// here: AccessControl has already reduced the policy to a per-field
/// <see cref="RecordFieldEnforcement"/>, and this type only applies it to the Deals wire
/// vocabulary. The representation rules are the ones frozen for Support: a withheld optional field is
/// omitted, a withheld required field fails the operation closed, MASKED is enforced as withheld, and
/// READ_ONLY blocks writes.
/// </summary>
internal static class DealFieldSecurity
{
    /// <summary>
    /// The field keys Deals can enforce a policy on, mapped to whether the wire contract makes
    /// the field required. These are the <c>DealReadModel</c> property names, taken from that record so the
    /// vocabulary cannot drift from what Deals actually projects.
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
            ["name"] = true,
            ["buyerRef"] = true,
            ["stageCode"] = true,
            ["stageCategory"] = true,
            ["amount"] = true,
            ["opportunityScore"] = true,
            ["ownerId"] = true,
            ["expectedCloseDate"] = true,
            ["interestedProductIds"] = true,
            ["lineItems"] = true,
            ["resourceVersion"] = true,
            ["createdAt"] = true,
            ["updatedAt"] = true,
            ["contactId"] = false,
            ["sourceLeadId"] = false,
            ["wonAt"] = false,
            ["lostAt"] = false,
            ["actualCloseDate"] = false,
            ["lostReason"] = false,
            ["notes"] = false,
            ["archivedAt"] = false,
            ["archiveReason"] = false,
            ["forecastCategory"] = false,
            ["forecastHistory"] = false,
            ["stageEnteredAt"] = false,
            ["nextActionAt"] = false,
            ["nextActionSummary"] = false,
            ["nextActionRef"] = false,
            ["winEvidence"] = false,
            ["lostReasonNote"] = false,
            ["recycleDecision"] = false,
            ["recycleEligible"] = false,
            ["revisitAt"] = false
        };

    internal static IReadOnlyList<string> FieldKeys { get; } = EnforceableFields.Keys.Order(StringComparer.Ordinal).ToArray();

    internal static DealReadModel Project(DealReadModel model, RecordAccessAuthorization access) =>
        model with
        {
            ContactId = access.CanRead("contactId") ? model.ContactId : null,
            SourceLeadId = access.CanRead("sourceLeadId") ? model.SourceLeadId : null,
            WonAt = access.CanRead("wonAt") ? model.WonAt : null,
            LostAt = access.CanRead("lostAt") ? model.LostAt : null,
            ActualCloseDate = access.CanRead("actualCloseDate") ? model.ActualCloseDate : null,
            LostReason = access.CanRead("lostReason") ? model.LostReason : null,
            Notes = access.CanRead("notes") ? model.Notes : null,
            ArchivedAt = access.CanRead("archivedAt") ? model.ArchivedAt : null,
            ArchiveReason = access.CanRead("archiveReason") ? model.ArchiveReason : null,
            ForecastCategory = access.CanRead("forecastCategory") ? model.ForecastCategory : null,
            ForecastHistory = access.CanRead("forecastHistory") ? model.ForecastHistory : null,
            StageEnteredAt = access.CanRead("stageEnteredAt") ? model.StageEnteredAt : null,
            NextActionAt = access.CanRead("nextActionAt") ? model.NextActionAt : null,
            NextActionSummary = access.CanRead("nextActionSummary") ? model.NextActionSummary : null,
            NextActionRef = access.CanRead("nextActionRef") ? model.NextActionRef : null,
            WinEvidence = access.CanRead("winEvidence") ? model.WinEvidence : null,
            LostReasonNote = access.CanRead("lostReasonNote") ? model.LostReasonNote : null,
            RecycleDecision = access.CanRead("recycleDecision") ? model.RecycleDecision : null,
            RecycleEligible = access.CanRead("recycleEligible") ? model.RecycleEligible : null,
            RevisitAt = access.CanRead("revisitAt") ? model.RevisitAt : null
        };

    /// <summary>
    /// The refusal a caller receives when a restrictive policy names a field the representation being
    /// returned cannot omit. AccessControl decides that, against the representation the operation
    /// declared; this owner only applies the answer.
    /// </summary>
    internal static DealOperationError? UnenforceablePolicy(RecordAccessAuthorization access) =>
        access.UnenforceableFieldKeys.Count == 0
            ? null
            : new DealOperationError(
                "ACCESS_DENIED",
                403,
                "Access denied",
                "A field-security policy applies to a field this resource cannot withhold, so the request is refused rather than returning a value the policy forbids.");

    internal static DealOperationError? GuardFieldWrite(RecordAccessAuthorization access, params string[] fieldKeys) =>
        Refusal(fieldKeys.Where(fieldKey => !access.CanWrite(fieldKey)).ToList());

    /// <summary>
    /// Refuses a profile replacement that would change a field the caller may not write. The check
    /// compares the requested profile against the stored aggregate, so replacing a field with the
    /// value it already holds is not a write and is not refused. Without this comparison a full
    /// profile replacement would either send every profile field through the write check - refusing
    /// unchanged READ_ONLY values - or send none, which is what let a READ_ONLY field be replaced.
    /// </summary>
    internal static DealOperationError? GuardProfileWrite(
        RecordAccessAuthorization access,
        DealProfile current,
        DealProfile requested)
    {
        var currentValues = Values(current);
        var blocked = new List<string>();
        foreach (var pair in Values(requested))
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
    internal static DealOperationError? GuardCreateWrite(
        RecordAccessAuthorization access,
        DealProfile profile,
        DateTimeOffset? nextActionAt,
        string? nextActionSummary,
        string? nextActionTaskId)
    {
        var blocked = new List<string>();
        foreach (var pair in Values(profile))
        {
            var written = RequiredCreateFields.Contains(pair.Key, StringComparer.Ordinal) || pair.Value.Length != 0;
            if (written && !access.CanWrite(pair.Key))
                blocked.Add(pair.Key);
        }

        // A create always sets the opening stage and a forecast category, and optionally a next
        // action.
        if (!access.CanWrite("stageCode")) blocked.Add("stageCode");
        if (!access.CanWrite("stageCategory")) blocked.Add("stageCategory");
        if (!access.CanWrite("forecastCategory")) blocked.Add("forecastCategory");
        if (nextActionAt is not null && !access.CanWrite("nextActionAt")) blocked.Add("nextActionAt");
        if (nextActionSummary is not null && !access.CanWrite("nextActionSummary")) blocked.Add("nextActionSummary");
        if (nextActionTaskId is not null && !access.CanWrite("nextActionRef")) blocked.Add("nextActionRef");
        return Refusal(blocked);
    }

    /// <summary>
    /// The create-contract fields a Deal always carries a value for. A non-writable required create
    /// field fails the creation closed: there is no admitted representation of a Deal created
    /// without a name, buyer, amount, score, owner or expected close date.
    /// </summary>
    private static readonly string[] RequiredCreateFields =
        ["name", "buyerRef", "amount", "opportunityScore", "ownerId", "expectedCloseDate", "interestedProductIds"];

    /// <summary>
    /// The profile as its wire field vocabulary, each value reduced to a canonical string so a change
    /// is decided by value and not by object identity. An empty string means the profile carries no
    /// value for that field.
    /// </summary>
    private static Dictionary<string, string> Values(DealProfile profile) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = profile.Name,
            ["buyerRef"] = $"{profile.BuyerRef.Type}|{profile.BuyerRef.Id}",
            ["amount"] = $"{profile.Amount.Amount}|{profile.Amount.Currency}",
            ["opportunityScore"] = profile.OpportunityScore,
            ["ownerId"] = profile.OwnerId,
            ["expectedCloseDate"] = profile.ExpectedCloseDate.ToString("O", CultureInfo.InvariantCulture),
            ["contactId"] = profile.ContactId ?? string.Empty,
            ["sourceLeadId"] = profile.SourceLeadId ?? string.Empty,
            ["interestedProductIds"] = string.Join(",", profile.InterestedProductIds),
            ["notes"] = profile.Notes ?? string.Empty
        };

    private static DealOperationError? Refusal(List<string> blocked) =>
        blocked.Count == 0
            ? null
            : new DealOperationError(
                "ACCESS_DENIED",
                403,
                "Access denied",
                $"Field security does not permit writing: {string.Join(", ", blocked.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))}.");
}

/// <summary>
/// The Deals application boundary of the trusted authority chain: authenticated user -> requested
/// Workspace -> verified membership -> trusted CurrentWorkspace -> capability authorization ->
/// record scope -> field security -> Deals use case.
///
/// <para>Everything beyond the capability check is decided by AccessControl through
/// <see cref="IRecordAccessEvaluator"/>. Deals holds no scope rule and no field rule of its own.</para>
/// </summary>
internal sealed class DealAuthorization(IRecordAccessEvaluator evaluator)
{
    internal const string ResourceKey = "deals";

    /// <param name="representation">
    /// The representation the calling operation will return. It decides only whether a restrictive
    /// policy on a field this resource declares required can be honoured by omitting the value; it
    /// can never widen read or write access. Operations returning the full read model pass
    /// <see cref="RecordAccessRepresentation.Full"/>, which is the default.
    /// </param>
    internal async Task<DealOperationResult<DealAccess>> AuthorizeAsync(
        AccessRequirement requirement,
        DealRequestMetadata metadata,
        CancellationToken cancellationToken,
        RecordAccessRepresentation? representation = null)
    {
        var authorization = await evaluator.AuthorizeResourceAsync(
            ResourceKey,
            requirement.Capability,
            DealFieldSecurity.FieldKeys,
            representation ?? RecordAccessRepresentation.Full,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);

        if (authorization.TrustedWorkspace is not { } trusted)
        {
            return DealOperationResult<DealAccess>.Failure(
                authorization.Code == "WORKSPACE_MISMATCH" ? DealErrors.WorkspaceMismatch() : DealErrors.AccessDenied());
        }

        if (!authorization.IsAllowed)
            return DealOperationResult<DealAccess>.Failure(DealErrors.AccessDenied());

        var unenforceable = DealFieldSecurity.UnenforceablePolicy(authorization);
        if (unenforceable is not null)
            return DealOperationResult<DealAccess>.Failure(unenforceable);

        return DealOperationResult<DealAccess>.Success(new DealAccess(trusted, authorization));
    }

    /// <summary>
    /// Enforces record scope against the Deals-owned authoritative fact.
    /// <c>DealProfile.OwnerId</c> is the member reference Deals records for a deal and is validated
    /// on write through the narrow Workspace active-member contract; nothing else in the aggregate is a
    /// member owner, so nothing else is substituted for one.
    /// A record outside scope is reported as not found.
    /// </summary>
    internal async Task<DealOperationError?> EnforceRecordAsync(
        DealAccess access,
        Deal record,
        string enforcementPoint,
        DealRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var decision = await evaluator.AuthorizeRecordAsync(
            access.Authorization,
            record.DealId,
            Facts(record),
            enforcementPoint,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);
        return decision.IsAllowed ? null : DealErrors.NotFound();
    }

    /// <summary>
    /// Authorizes the fields a command is about to write. It is deliberately separate from the
    /// record guard and is applied only on the new-execution path: record scope is current
    /// authorization and must gate a replay, whereas a replay performs no write at all and must not
    /// be refused for lacking permission to write what was already written.
    /// </summary>
    internal static DealOperationError? EnforceFieldWrite(DealAccess access, params string[] writtenFieldKeys) =>
        writtenFieldKeys.Length == 0
            ? null
            : DealFieldSecurity.GuardFieldWrite(access.Authorization, writtenFieldKeys);

    internal static RecordAccessFacts Facts(Deal record)
    {
        return RecordAccessFacts.Found(record.Profile.OwnerId);
    }
}
