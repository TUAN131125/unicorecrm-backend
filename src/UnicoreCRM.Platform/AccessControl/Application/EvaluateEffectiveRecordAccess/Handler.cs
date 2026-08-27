using UnicoreCRM.Platform.AccessControl.Application.Common;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.AccessControl.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Application.EvaluateEffectiveRecordAccess;

/// <summary>
/// Evaluates authoritative object-level access for one record.
///
/// The authority chain is preserved end to end: authenticated identity -> trusted Workspace ->
/// capability authorization -> authoritative record facts from the owning module -> record-scope
/// evaluation -> field-security projection -> immutable AccessControl decision evidence. Record
/// access is strictly additional to capability authorization and can never grant a capability the
/// caller does not hold.
///
/// A record the caller may not see produces a successful evaluation whose document denies
/// everything. A record that does not exist, a record owned by another Workspace and a record
/// hidden by scope therefore return identical documents, so no status code or payload difference
/// discloses foreign or hidden existence.
/// </summary>
internal sealed class Handler(
    IAccessAuthorizer authorizer,
    ICurrentWorkspace currentWorkspace,
    RecordAccessFactProviderRegistry providers,
    IAccessControlPersistence persistence,
    TimeProvider timeProvider)
{
    private const string DecisionSource = "access-control";

    internal async Task<AccessOperationResult<EffectiveRecordAccessDocument>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        if (!Validator.TryValidate(query.Request, out var request, out var fieldErrors))
            return AccessOperationResult<EffectiveRecordAccessDocument>.Failure(AccessErrors.Validation(fieldErrors!));

        var decision = await authorizer.AuthorizeAsync(
            AccessCapabilities.WorkspaceContextResolve,
            query.CorrelationId,
            cancellationToken);
        if (!decision.IsAllowed || decision.Context is not { } context)
        {
            return AccessOperationResult<EffectiveRecordAccessDocument>.Failure(
                decision.Code == "WORKSPACE_MISMATCH" ? AccessErrors.WorkspaceMismatch() : AccessErrors.AccessDenied());
        }

        var trusted = currentWorkspace.Require();
        var reasons = new List<EffectiveAccessDecisionReasonDocument>(6);
        var provider = providers.Find(request!.ResourceKey);
        if (provider is null)
        {
            // No business owner is authoritative for this resource key, so no record fact can be
            // established. Deny by default rather than falling back to capability-only access.
            reasons.Add(new EffectiveAccessDecisionReasonDocument(
                "RESOURCE_FACT_AUTHORITY_UNAVAILABLE",
                "DENY",
                "No authoritative record-fact owner is registered for this resource key.",
                DecisionSource));
            return await DenyAsync(query, request, trusted, reasons, cancellationToken);
        }

        var descriptor = provider.Descriptor;
        var capabilities = context.Capabilities;
        var hasRead = capabilities.Contains(descriptor.ReadCapability, StringComparer.Ordinal);
        var scope = RecordAccessPolicy.ResolveScope(ToScopePolicies(context.DataScopes), descriptor.ResourceKey);

        var facts = RecordAccessFacts.NotFound;
        if (hasRead && request.RecordId is not null)
        {
            // The owner is consulted only after capability authorization allows the read, so a
            // caller without the capability never causes a business lookup for a record.
            facts = await provider.ReadFactsAsync(
                trusted,
                request.RecordId,
                new RecordAccessRequestContext(query.RequestId, query.CorrelationId),
                cancellationToken);
        }

        var scopeDecision = RecordAccessPolicy.EvaluateScope(
            scope,
            recordRequested: request.RecordId is not null,
            recordFound: facts.Status == RecordAccessFactStatus.Found,
            facts.OwnerMemberId,
            trusted.MemberId);

        var canRead = hasRead && scopeDecision.Outcome != RecordScopeOutcome.Denied;
        var canUpdate = canRead && Holds(capabilities, descriptor.UpdateCapability);
        var canDelete = canRead && Holds(capabilities, descriptor.DeleteCapability);
        var canExport = canRead && request.IncludeExport && Holds(capabilities, descriptor.ExportCapability);
        var canApprove = canRead && request.IncludeApproval && Holds(capabilities, descriptor.ApproveCapability);

        var allowedCommands = new List<string>();
        if (canRead)
        {
            foreach (var command in request.RequestedCommands)
            {
                if (descriptor.CommandCapabilities.TryGetValue(command, out var required)
                    && capabilities.Contains(required, StringComparer.Ordinal))
                {
                    allowedCommands.Add(command);
                }
            }
        }

        var fieldSecurity = ToFieldPolicies(context.FieldSecurity);
        var fieldAccess = new Dictionary<string, string>(StringComparer.Ordinal);
        var restricted = 0;
        foreach (var fieldKey in request.RequestedFields)
        {
            var resolved = RecordAccessPolicy.ResolveFieldAccess(fieldSecurity, descriptor.ResourceKey, fieldKey);
            var capped = RecordAccessPolicy.Cap(resolved, canRead, canUpdate);
            if (capped != AccessFieldAccess.ReadWrite)
                restricted++;
            fieldAccess[fieldKey] = AccessProjection.ToWireValue(capped);
        }

        var decisionCode = BuildReasons(
            reasons,
            hasRead,
            scopeDecision,
            canRead,
            allowedCommands.Count,
            request.RequestedCommands.Count,
            restricted);

        var now = timeProvider.GetUtcNow();
        persistence.AddRecordDecision(new RecordAccessDecisionRecord(
            trusted.WorkspaceId,
            trusted.MembershipId,
            trusted.MemberId,
            descriptor.ResourceKey,
            request.RecordId,
            descriptor.ReadCapability,
            canRead,
            ScopeEvidence(scopeDecision),
            decisionCode,
            query.RequestId,
            query.CorrelationId,
            scopeDecision.OwnerMatch,
            now));
        await persistence.SaveChangesAsync(cancellationToken);

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
            now,
            "backend"));
    }

    /// <summary>
    /// The fully denied projection. Every requested field collapses to HIDDEN and every command is
    /// withheld, so the response carries no signal about the record beyond the caller's own input.
    /// </summary>
    private async Task<AccessOperationResult<EffectiveRecordAccessDocument>> DenyAsync(
        Query query,
        ValidatedRecordAccessRequest request,
        TrustedWorkspaceContext trusted,
        List<EffectiveAccessDecisionReasonDocument> reasons,
        CancellationToken cancellationToken)
    {
        var fieldAccess = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var fieldKey in request.RequestedFields)
            fieldAccess[fieldKey] = AccessProjection.ToWireValue(AccessFieldAccess.Hidden);

        var now = timeProvider.GetUtcNow();
        persistence.AddRecordDecision(new RecordAccessDecisionRecord(
            trusted.WorkspaceId,
            trusted.MembershipId,
            trusted.MemberId,
            request.ResourceKey,
            request.RecordId,
            "-",
            false,
            "UNAVAILABLE",
            "RESOURCE_FACT_AUTHORITY_UNAVAILABLE",
            query.RequestId,
            query.CorrelationId,
            null,
            now));
        await persistence.SaveChangesAsync(cancellationToken);

        return AccessOperationResult<EffectiveRecordAccessDocument>.Success(new EffectiveRecordAccessDocument(
            trusted.WorkspaceId,
            request.ResourceKey,
            request.RecordId,
            false,
            false,
            false,
            false,
            false,
            [],
            fieldAccess,
            reasons,
            now,
            "backend"));
    }

    private static string BuildReasons(
        List<EffectiveAccessDecisionReasonDocument> reasons,
        bool hasRead,
        RecordScopeDecision scopeDecision,
        bool canRead,
        int allowedCommandCount,
        int requestedCommandCount,
        int restrictedFieldCount)
    {
        if (!hasRead)
        {
            reasons.Add(new EffectiveAccessDecisionReasonDocument(
                "CAPABILITY_DENIED",
                "DENY",
                "The membership does not hold the resource read capability.",
                DecisionSource));
            return "CAPABILITY_DENIED";
        }

        reasons.Add(new EffectiveAccessDecisionReasonDocument("CAPABILITY_GRANTED", "ALLOW", null, DecisionSource));

        string code;
        switch (scopeDecision.Outcome)
        {
            case RecordScopeOutcome.NotEvaluated:
                reasons.Add(new EffectiveAccessDecisionReasonDocument(
                    "RECORD_SCOPE_NOT_EVALUATED",
                    "LIMIT",
                    "No record identifier was supplied, so the decision is resource-level only.",
                    DecisionSource));
                code = "RECORD_SCOPE_NOT_EVALUATED";
                break;
            case RecordScopeOutcome.Allowed:
                code = scopeDecision.Scope == AccessDataScope.Own
                    ? "RECORD_SCOPE_OWN_MATCHED"
                    : "RECORD_SCOPE_WORKSPACE";
                reasons.Add(new EffectiveAccessDecisionReasonDocument(code, "ALLOW", null, DecisionSource));
                break;
            default:
                // One code, one message and one source for a missing record, a foreign-Workspace
                // record, an owner mismatch and an unsupported scope. Splitting them would let a
                // caller probe existence.
                reasons.Add(new EffectiveAccessDecisionReasonDocument(
                    "RECORD_ACCESS_DENIED",
                    "DENY",
                    "The record is not available to this membership.",
                    DecisionSource));
                code = "RECORD_ACCESS_DENIED";
                break;
        }

        if (canRead && allowedCommandCount < requestedCommandCount)
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

        return code;
    }

    private static string ScopeEvidence(RecordScopeDecision decision) =>
        decision.Outcome == RecordScopeOutcome.NotEvaluated
            ? "NOT_EVALUATED"
            : AccessProjection.ToWireValue(decision.Scope);

    private static bool Holds(IReadOnlyList<string> capabilities, string? capability) =>
        capability is not null && capabilities.Contains(capability, StringComparer.Ordinal);

    private static IReadOnlyList<EffectiveDataScopePolicy> ToScopePolicies(IReadOnlyList<AuthorizationDataScopeEntry> entries)
    {
        var result = new List<EffectiveDataScopePolicy>(entries.Count);
        foreach (var entry in entries)
            result.Add(new EffectiveDataScopePolicy(entry.ResourceKey, ParseScope(entry.Scope)));
        return result;
    }

    private static IReadOnlyList<EffectiveFieldSecurityPolicy> ToFieldPolicies(IReadOnlyList<AuthorizationFieldAccessEntry> entries)
    {
        var result = new List<EffectiveFieldSecurityPolicy>(entries.Count);
        foreach (var entry in entries)
            result.Add(new EffectiveFieldSecurityPolicy(entry.ResourceKey, entry.FieldKey, ParseFieldAccess(entry.Access)));
        return result;
    }

    // The projected context is the module's own wire vocabulary, so an unrecognised value can only
    // mean the projection gained a state this evaluator has not admitted. Both parsers therefore
    // fall back to the most restrictive interpretation.
    private static AccessDataScope ParseScope(string scope) => scope switch
    {
        "OWN" => AccessDataScope.Own,
        "TEAM" => AccessDataScope.Team,
        "WORKSPACE" => AccessDataScope.Workspace,
        _ => AccessDataScope.Custom
    };

    private static AccessFieldAccess ParseFieldAccess(string access) => access switch
    {
        "READ_WRITE" => AccessFieldAccess.ReadWrite,
        "READ_ONLY" => AccessFieldAccess.ReadOnly,
        "MASKED" => AccessFieldAccess.Masked,
        _ => AccessFieldAccess.Hidden
    };
}
