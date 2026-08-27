using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.AccessControl.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Application.Common;

/// <summary>
/// The one record-access authority. Both the public `POST /access/records/evaluate` operation and
/// every business owner that enforces record access go through this type, so what a consumer is
/// told and what the server enforces are produced by the same code path and cannot drift.
///
/// <para>It performs the capability authorization itself - exactly once per call - so an owner that
/// authorizes through it does not also authorize separately and write a second decision row.</para>
/// </summary>
internal sealed class RecordAccessEvaluator(
    IAccessAuthorizer authorizer,
    ICurrentWorkspace currentWorkspace,
    RecordAccessFactProviderRegistry providers,
    IAccessControlPersistence persistence,
    TimeProvider timeProvider) : IRecordAccessEvaluator
{
    public async Task<RecordAccessAuthorization> AuthorizeResourceAsync(
        string resourceKey,
        string requiredCapability,
        IReadOnlyList<string>? requestedFields,
        RecordAccessRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredCapability);
        ArgumentNullException.ThrowIfNull(requestContext);

        var decision = await authorizer.AuthorizeAsync(
            AccessCapabilities.WorkspaceContextResolve,
            requestContext.CorrelationId,
            cancellationToken);
        if (!decision.IsAllowed || decision.Context is not { } context)
            return Denied(decision.Code, resourceKey, requiredCapability);

        var trusted = currentWorkspace.Require();
        var descriptor = providers.Find(resourceKey)?.Descriptor;
        var canonicalResourceKey = descriptor?.ResourceKey ?? resourceKey;
        var capabilities = context.Capabilities;
        var holdsCapability = capabilities.Contains(requiredCapability, StringComparer.Ordinal);

        var dataScopes = ToScopePolicies(context.DataScopes);
        var scope = RecordAccessPolicy.ResolveScope(dataScopes, canonicalResourceKey);
        var (filter, scopeOwner) = Filter(scope, holdsCapability, trusted);

        var fieldSecurity = ToFieldPolicies(context.FieldSecurity);
        var enforcement = new Dictionary<string, RecordFieldEnforcement>(RecordAccessKey.Comparer);
        var unenforceable = new List<string>();
        foreach (var fieldKey in requestedFields ?? [])
        {
            var access = RecordAccessPolicy.ResolveFieldAccess(fieldSecurity, canonicalResourceKey, fieldKey);
            enforcement[fieldKey] = Enforcement(access);
            if (access is AccessFieldAccess.Hidden or AccessFieldAccess.Masked
                && !CanWithhold(descriptor, fieldKey))
            {
                unenforceable.Add(fieldKey);
            }
        }

        return new RecordAccessAuthorization(
            holdsCapability,
            holdsCapability ? "AUTHORIZED" : AccessErrors.AccessDenied().Code,
            trusted,
            filter,
            scopeOwner,
            AccessProjection.ToWireValue(scope),
            enforcement,
            unenforceable,
            capabilities,
            Fingerprint(context),
            canonicalResourceKey,
            requiredCapability);
    }

    public async Task<RecordAccessRecordDecision> AuthorizeRecordAsync(
        RecordAccessAuthorization authorization,
        string recordId,
        RecordAccessFacts facts,
        string enforcementPoint,
        RecordAccessRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);

        // Record scope is additional to capability and can never restore one: a caller denied the
        // capability is denied the record no matter who owns it.
        var scopeDecision = authorization.IsAllowed && authorization.TrustedWorkspace is { } trusted
            ? RecordAccessPolicy.EvaluateScope(
                ParseScope(authorization.EvaluatedScope),
                recordRequested: true,
                recordFound: facts.Status == RecordAccessFactStatus.Found,
                facts.OwnerMemberId,
                trusted.MemberId)
            : new RecordScopeDecision(RecordScopeOutcome.Denied, AccessDataScope.Custom, null);

        var allowed = authorization.IsAllowed && scopeDecision.Outcome == RecordScopeOutcome.Allowed;
        var code = !authorization.IsAllowed
            ? "CAPABILITY_DENIED"
            : allowed
                ? (scopeDecision.Scope == AccessDataScope.Own ? "RECORD_SCOPE_OWN_MATCHED" : "RECORD_SCOPE_WORKSPACE")
                : "RECORD_ACCESS_DENIED";

        if (authorization.TrustedWorkspace is { } workspace)
        {
            await WriteDecisionAsync(
                workspace,
                authorization,
                recordId,
                allowed,
                authorization.EvaluatedScope,
                code,
                enforcementPoint,
                scopeDecision.OwnerMatch,
                requestContext,
                cancellationToken);
        }

        return new RecordAccessRecordDecision(allowed, authorization.EvaluatedScope, scopeDecision.OwnerMatch);
    }

    /// <summary>
    /// Appends the AccessControl-owned decision evidence. It is written on the AccessControl
    /// context, so it is never part of a business owner's transaction and a denied evaluation
    /// stages no business change.
    /// </summary>
    internal async Task WriteDecisionAsync(
        TrustedWorkspaceContext trusted,
        RecordAccessAuthorization authorization,
        string? recordId,
        bool allowed,
        string evaluatedScope,
        string decisionCode,
        string enforcementPoint,
        bool? ownerMatch,
        RecordAccessRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        persistence.AddRecordDecision(new RecordAccessDecisionRecord(
            trusted.WorkspaceId,
            trusted.MembershipId,
            trusted.MemberId,
            authorization.ResourceKey,
            recordId,
            authorization.RequiredCapability,
            allowed,
            evaluatedScope,
            decisionCode,
            requestContext.RequestId,
            requestContext.CorrelationId,
            ownerMatch,
            enforcementPoint,
            authorization.PolicyFingerprint,
            RestrictedFieldEvidence(authorization),
            timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
    }

    internal static AccessFieldAccess ResolveFieldAccess(
        IReadOnlyList<AuthorizationFieldAccessEntry> entries,
        string resourceKey,
        string fieldKey) =>
        RecordAccessPolicy.ResolveFieldAccess(ToFieldPolicies(entries), resourceKey, fieldKey);

    /// <summary>
    /// The decision-relevant field restrictions, recorded so a later policy change can be told
    /// apart from a different decision. Field keys are policy identifiers, not business values.
    /// </summary>
    private static string RestrictedFieldEvidence(RecordAccessAuthorization authorization)
    {
        var restricted = authorization.FieldEnforcement
            .Where(pair => pair.Value != RecordFieldEnforcement.ReadWrite)
            .OrderBy(pair => pair.Key, RecordAccessKey.Comparer)
            .Select(pair => $"{pair.Key}:{pair.Value}");
        var evidence = string.Join(",", restricted);
        return evidence.Length <= 2000 ? evidence : evidence[..2000];
    }

    /// <summary>
    /// A deterministic digest of the effective policy the decision was taken against. No policy
    /// revision or version is admitted anywhere in current authority, so this is the minimum that
    /// still lets two decisions be compared: identical fingerprints mean identical effective
    /// policy, and a changed fingerprint marks the decision as taken under different policy.
    /// </summary>
    private static string Fingerprint(AuthorizationContextDocument context)
    {
        var builder = new StringBuilder();
        foreach (var capability in context.Capabilities)
            builder.Append("c|").Append(capability).Append('\n');
        foreach (var scope in context.DataScopes.OrderBy(item => item.ResourceKey, RecordAccessKey.Comparer))
            builder.Append("s|").Append(scope.ResourceKey).Append('|').Append(scope.Scope).Append('\n');
        foreach (var field in context.FieldSecurity
                     .OrderBy(item => item.ResourceKey, RecordAccessKey.Comparer)
                     .ThenBy(item => item.FieldKey, RecordAccessKey.Comparer))
        {
            builder.Append("f|").Append(field.ResourceKey).Append('|').Append(field.FieldKey).Append('|').Append(field.Access).Append('\n');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash).ToLower(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Whether the owner can honour a withheld value for this field. It cannot when it does not
    /// declare the field, and it cannot when the wire contract makes the field required, because no
    /// admitted representation exists for a required field whose value must not be exposed.
    /// </summary>
    private static bool CanWithhold(RecordAccessResourceDescriptor? descriptor, string fieldKey) =>
        descriptor is not null
        && descriptor.EnforceableFields.TryGetValue(fieldKey, out var required)
        && !required;

    private static RecordFieldEnforcement Enforcement(AccessFieldAccess access) => access switch
    {
        AccessFieldAccess.ReadWrite => RecordFieldEnforcement.ReadWrite,
        AccessFieldAccess.ReadOnly => RecordFieldEnforcement.ReadOnly,
        _ => RecordFieldEnforcement.Withheld
    };

    private static (RecordAccessScopeFilter Filter, string? OwnerMemberId) Filter(
        AccessDataScope scope,
        bool holdsCapability,
        TrustedWorkspaceContext trusted)
    {
        if (!holdsCapability)
            return (RecordAccessScopeFilter.Denied, null);
        return scope switch
        {
            AccessDataScope.Workspace => (RecordAccessScopeFilter.Workspace, null),
            AccessDataScope.Own => (RecordAccessScopeFilter.OwnedByMember, trusted.MemberId),
            // TEAM has no authoritative team ownership or membership behind it and CUSTOM has no
            // admitted allowed-owner semantics, so neither is widened - both deny every record.
            _ => (RecordAccessScopeFilter.Denied, null)
        };
    }

    private static RecordAccessAuthorization Denied(string code, string resourceKey, string requiredCapability) =>
        new(
            false,
            code,
            null,
            RecordAccessScopeFilter.Denied,
            null,
            "NOT_EVALUATED",
            new Dictionary<string, RecordFieldEnforcement>(RecordAccessKey.Comparer),
            [],
            [],
            string.Empty,
            resourceKey,
            requiredCapability);

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
    internal static AccessDataScope ParseScope(string scope) => scope switch
    {
        "OWN" => AccessDataScope.Own,
        "TEAM" => AccessDataScope.Team,
        "WORKSPACE" => AccessDataScope.Workspace,
        _ => AccessDataScope.Custom
    };

    internal static AccessFieldAccess ParseFieldAccess(string access) => access switch
    {
        "READ_WRITE" => AccessFieldAccess.ReadWrite,
        "READ_ONLY" => AccessFieldAccess.ReadOnly,
        "MASKED" => AccessFieldAccess.Masked,
        _ => AccessFieldAccess.Hidden
    };
}
