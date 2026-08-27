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
    /// vocabulary cannot drift from what Deals actually projects. A policy naming any other key
    /// cannot be enforced and fails the operation closed rather than being silently ignored.
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

    internal static DealOperationError? UnenforceablePolicy(RecordAccessAuthorization access) =>
        access.UnenforceableFieldKeys.Count == 0
            ? null
            : new DealOperationError(
                "ACCESS_DENIED",
                403,
                "Access denied",
                "A field-security policy applies to a field this resource cannot withhold, so the request is refused rather than returning a value the policy forbids.");

    internal static DealOperationError? GuardFieldWrite(RecordAccessAuthorization access, params string[] fieldKeys)
    {
        var blocked = fieldKeys.Where(fieldKey => !access.CanWrite(fieldKey)).Order(StringComparer.Ordinal).ToArray();
        return blocked.Length == 0
            ? null
            : new DealOperationError(
                "ACCESS_DENIED",
                403,
                "Access denied",
                $"Field security does not permit writing: {string.Join(", ", blocked)}.");
    }
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

    internal async Task<DealOperationResult<DealAccess>> AuthorizeAsync(
        AccessRequirement requirement,
        DealRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var authorization = await evaluator.AuthorizeResourceAsync(
            ResourceKey,
            requirement.Capability,
            DealFieldSecurity.FieldKeys,
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
    /// <param name="writtenFieldKeys">
    /// The wire fields the command would change. They are checked only after record scope allows the
    /// record, so a hidden record is reported as missing rather than leaking a field-policy refusal.
    /// </param>
    internal async Task<DealOperationError?> EnforceRecordAsync(
        DealAccess access,
        Deal record,
        string enforcementPoint,
        DealRequestMetadata metadata,
        CancellationToken cancellationToken,
        params string[] writtenFieldKeys)
    {
        var decision = await evaluator.AuthorizeRecordAsync(
            access.Authorization,
            record.DealId,
            Facts(record),
            enforcementPoint,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);
        if (!decision.IsAllowed)
            return DealErrors.NotFound();
        return writtenFieldKeys.Length == 0
            ? null
            : DealFieldSecurity.GuardFieldWrite(access.Authorization, writtenFieldKeys);
    }

    internal static RecordAccessFacts Facts(Deal record)
    {
        return RecordAccessFacts.Found(record.Profile.OwnerId);
    }
}
