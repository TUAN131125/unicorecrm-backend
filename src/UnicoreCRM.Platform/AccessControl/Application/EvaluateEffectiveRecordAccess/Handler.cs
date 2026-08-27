using UnicoreCRM.Platform.AccessControl.Application.Common;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.AccessControl.Domain;

namespace UnicoreCRM.Platform.AccessControl.Application.EvaluateEffectiveRecordAccess;

/// <summary>
/// Reports authoritative object-level access for one record.
///
/// <para>This operation only <em>reports</em> a decision. It is not the enforcement point, and a
/// consumer that never calls it is not thereby unprotected: every business owner enforces the same
/// decision through <see cref="IRecordAccessEvaluator"/>, which is the same authority this handler
/// uses. The two therefore cannot diverge.</para>
///
/// <para>A record the caller may not see produces a successful evaluation whose document denies
/// everything. A record that does not exist, a record owned by another Workspace and a record
/// hidden by scope return identical documents, so no status code or payload difference discloses
/// foreign or hidden existence.</para>
/// </summary>
internal sealed class Handler(
    IAccessContextAuthorizer authorizer,
    IRecordAccessEvaluator evaluator,
    RecordAccessEvaluator decisionWriter,
    RecordAccessFactProviderRegistry providers,
    TimeProvider timeProvider)
{
    private const string DecisionSource = "access-control";
    private const string EnforcementPoint = "evaluateEffectiveRecordAccess";

    internal async Task<AccessOperationResult<EffectiveRecordAccessDocument>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        if (!Validator.TryValidate(query.Request, out var request, out var fieldErrors))
            return AccessOperationResult<EffectiveRecordAccessDocument>.Failure(AccessErrors.Validation(fieldErrors!));

        var context = new RecordAccessRequestContext(query.RequestId, query.CorrelationId);

        // This operation has an operation capability of its own - `workspace.context.resolve` - and
        // it is not the same question as the resource capability below. Asking to be told what one
        // may do with a record is a context question; whether that record may actually be read is a
        // resource question. Both are audited under the capability they really evaluated, and the
        // resource half is the single evaluation every business owner enforces through, so what this
        // operation reports and what the server enforces still come from one authority.
        var operation = await authorizer.AuthorizeWithContextAsync(
            AccessCapabilities.WorkspaceContextResolve, query.CorrelationId, cancellationToken);
        if (!operation.IsAllowed)
        {
            return AccessOperationResult<EffectiveRecordAccessDocument>.Failure(
                operation.Code == "WORKSPACE_MISMATCH" ? AccessErrors.WorkspaceMismatch() : AccessErrors.AccessDenied());
        }

        var provider = providers.Find(request!.ResourceKey);
        var descriptor = provider?.Descriptor;

        // The read capability is the owner's own declaration. With no registered owner there is no
        // capability vocabulary and no authoritative fact, so the evaluation denies by default.
        var readCapability = descriptor?.ReadCapability ?? UnresolvableCapability;
        var authorization = await evaluator.AuthorizeResourceAsync(
            request.ResourceKey,
            readCapability,
            request.RequestedFields,
            context,
            cancellationToken);

        if (authorization.TrustedWorkspace is not { } trusted)
        {
            return AccessOperationResult<EffectiveRecordAccessDocument>.Failure(
                authorization.Code == "WORKSPACE_MISMATCH" ? AccessErrors.WorkspaceMismatch() : AccessErrors.AccessDenied());
        }

        var reasons = new List<EffectiveAccessDecisionReasonDocument>(6);
        if (descriptor is null)
        {
            reasons.Add(new EffectiveAccessDecisionReasonDocument(
                "RESOURCE_FACT_AUTHORITY_UNAVAILABLE",
                "DENY",
                "No authoritative record-fact owner is registered for this resource key.",
                DecisionSource));
            await decisionWriter.WriteDecisionAsync(
                trusted, authorization, request.RecordId, false, "UNAVAILABLE",
                "RESOURCE_FACT_AUTHORITY_UNAVAILABLE", EnforcementPoint, null, context, cancellationToken);
            return Denied(trusted.WorkspaceId, request, reasons, timeProvider.GetUtcNow());
        }

        var hasRead = authorization.IsAllowed;
        RecordAccessRecordDecision? recordDecision = null;
        if (hasRead && request.RecordId is not null)
        {
            // The owner is consulted only after capability authorization allows the read, so a
            // caller without the capability never causes a business lookup for a record.
            var facts = await provider!.ReadFactsAsync(trusted, request.RecordId, context, cancellationToken);
            recordDecision = await evaluator.AuthorizeRecordAsync(
                authorization, request.RecordId, facts, EnforcementPoint, context, cancellationToken);
        }

        var recordEvaluated = request.RecordId is not null;
        var canRead = hasRead && (!recordEvaluated || recordDecision!.IsAllowed);

        // Resource-level and record-level questions are answered differently on purpose. Without a
        // record identifier the caller is asking what the resource permits, so a command is granted
        // from its own capability alone - `support.create` must not silently require `support.read`.
        // With a record identifier the commands target that record, so they additionally require it
        // to be readable and in scope. This is the same rule `AuthorizeRecordAsync` enforces for
        // every owner, so what this operation reports and what the server enforces cannot drift.
        var commandGate = recordEvaluated ? canRead : true;
        var allowedCommands = new List<string>();
        if (commandGate)
        {
            foreach (var command in request.RequestedCommands)
            {
                if (descriptor.CommandCapabilities.TryGetValue(command, out var required)
                    && authorization.Holds(required))
                {
                    allowedCommands.Add(command);
                }
            }
        }

        var canUpdate = canRead && authorization.Holds(descriptor.UpdateCapability ?? string.Empty);
        var canDelete = canRead && authorization.Holds(descriptor.DeleteCapability ?? string.Empty);
        var canExport = canRead && request.IncludeExport && authorization.Holds(descriptor.ExportCapability ?? string.Empty);
        var canApprove = canRead && request.IncludeApproval && authorization.Holds(descriptor.ApproveCapability ?? string.Empty);

        var fieldAccess = new Dictionary<string, string>(StringComparer.Ordinal);
        var restricted = 0;
        foreach (var fieldKey in request.RequestedFields)
        {
            // A field key the owner does not declare has no enforcement entry, and the answer for it
            // is withheld rather than widened. Reporting READ_WRITE for an unrecognised key would
            // let a typo in a consumer's field list read as a grant.
            var enforcement = authorization.FieldEnforcement.TryGetValue(fieldKey, out var value)
                ? value
                : RecordFieldEnforcement.Withheld;
            var wire = Wire(enforcement, canRead, canUpdate);
            if (wire != "READ_WRITE")
                restricted++;
            fieldAccess[fieldKey] = wire;
        }

        BuildReasons(reasons, hasRead, recordEvaluated, recordDecision, canRead, allowedCommands.Count, request.RequestedCommands.Count, restricted, authorization.UnenforceableFieldKeys.Count);

        return AccessOperationResult<EffectiveRecordAccessDocument>.Success(new EffectiveRecordAccessDocument(
            trusted.WorkspaceId,
            descriptor.ResourceKey,
            request.RecordId,
            canRead,
            canUpdate,
            canDelete,
            canExport,
            canApprove,
            allowedCommands,
            fieldAccess,
            reasons,
            timeProvider.GetUtcNow(),
            "backend"));
    }

    /// <summary>
    /// A capability identifier no role can hold, used when no owner declares one. It is deliberately
    /// not a guessed `{resourceKey}.read`: inferring a capability name would let an unowned resource
    /// resolve against a real capability that governs something else.
    /// </summary>
    private const string UnresolvableCapability = "unresolvable.record.access";

    /// <summary>
    /// The fully denied projection. Every requested field collapses to HIDDEN and every command is
    /// withheld, so the response carries no signal about the record beyond the caller's own input.
    /// </summary>
    private static AccessOperationResult<EffectiveRecordAccessDocument> Denied(
        string workspaceId,
        ValidatedRecordAccessRequest request,
        IReadOnlyList<EffectiveAccessDecisionReasonDocument> reasons,
        DateTimeOffset now)
    {
        var fieldAccess = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var fieldKey in request.RequestedFields)
            fieldAccess[fieldKey] = AccessProjection.ToWireValue(AccessFieldAccess.Hidden);

        return AccessOperationResult<EffectiveRecordAccessDocument>.Success(new EffectiveRecordAccessDocument(
            workspaceId,
            request.ResourceKey,
            request.RecordId,
            false, false, false, false, false,
            [],
            fieldAccess,
            reasons,
            now,
            "backend"));
    }

    private static string Wire(RecordFieldEnforcement enforcement, bool canRead, bool canUpdate)
    {
        // A field is never more permissive than the record it belongs to.
        if (!canRead)
            return "HIDDEN";
        return enforcement switch
        {
            RecordFieldEnforcement.Withheld => "HIDDEN",
            RecordFieldEnforcement.ReadOnly => "READ_ONLY",
            _ => canUpdate ? "READ_WRITE" : "READ_ONLY"
        };
    }

    private static void BuildReasons(
        List<EffectiveAccessDecisionReasonDocument> reasons,
        bool hasRead,
        bool recordEvaluated,
        RecordAccessRecordDecision? recordDecision,
        bool canRead,
        int allowedCommandCount,
        int requestedCommandCount,
        int restrictedFieldCount,
        int unenforceableFieldCount)
    {
        if (!hasRead)
        {
            reasons.Add(new EffectiveAccessDecisionReasonDocument(
                "CAPABILITY_DENIED",
                "DENY",
                "The membership does not hold the resource read capability.",
                DecisionSource));
        }
        else
        {
            reasons.Add(new EffectiveAccessDecisionReasonDocument("CAPABILITY_GRANTED", "ALLOW", null, DecisionSource));

            if (!recordEvaluated)
            {
                reasons.Add(new EffectiveAccessDecisionReasonDocument(
                    "RECORD_SCOPE_NOT_EVALUATED",
                    "LIMIT",
                    "No record identifier was supplied, so the decision is resource-level only.",
                    DecisionSource));
            }
            else if (recordDecision!.IsAllowed)
            {
                var code = recordDecision.OwnerMatch is true ? "RECORD_SCOPE_OWN_MATCHED" : "RECORD_SCOPE_WORKSPACE";
                reasons.Add(new EffectiveAccessDecisionReasonDocument(code, "ALLOW", null, DecisionSource));
            }
            else
            {
                // One code, one message and one source for a missing record, a foreign-Workspace
                // record, an owner mismatch and an unsupported scope. Splitting them would let a
                // caller probe existence.
                reasons.Add(new EffectiveAccessDecisionReasonDocument(
                    "RECORD_ACCESS_DENIED",
                    "DENY",
                    "The record is not available to this membership.",
                    DecisionSource));
            }
        }

        if (allowedCommandCount < requestedCommandCount)
        {
            reasons.Add(new EffectiveAccessDecisionReasonDocument(
                "COMMAND_CAPABILITY_DENIED",
                "LIMIT",
                "Some requested commands are not granted to this membership.",
                DecisionSource));
        }

        if (canRead && restrictedFieldCount > 0)
        {
            reasons.Add(new EffectiveAccessDecisionReasonDocument(
                "FIELD_ACCESS_RESTRICTED",
                "LIMIT",
                "Some requested fields carry a read or write restriction.",
                DecisionSource));
        }

        if (unenforceableFieldCount > 0)
        {
            // Surfaced rather than hidden: the owner will fail this record closed, so a consumer
            // told only that the field is restricted would not understand the refusal it then gets.
            reasons.Add(new EffectiveAccessDecisionReasonDocument(
                "FIELD_POLICY_UNENFORCEABLE",
                "DENY",
                "A restrictive field policy names a field the owner cannot withhold, so the owner fails the record closed.",
                DecisionSource));
        }
    }
}
