using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Contracts;

/// <summary>
/// How a business owner must narrow its own query so the caller sees only records inside the
/// caller's effective record scope. AccessControl decides which filter applies; the owner only
/// applies it. An owner must never re-derive the scope rule from the policy projection.
/// </summary>
public enum RecordAccessScopeFilter
{
    /// <summary>No record is in scope. The owner returns an empty result, not a filtered one.</summary>
    Denied = 0,

    /// <summary>Every record of the trusted Workspace is in scope.</summary>
    Workspace = 1,

    /// <summary>Only records whose owner member reference equals <c>ScopeOwnerMemberId</c>.</summary>
    OwnedByMember = 2,

    /// <summary>No record identifier is in play, so record scope was deliberately not evaluated.</summary>
    NotEvaluated = 3
}

/// <summary>
/// What the caller may do with one field of one resource, after the record decision has capped it.
/// </summary>
public enum RecordFieldEnforcement
{
    /// <summary>The value must not leave the owner, and the field must not be written.</summary>
    Withheld = 0,

    /// <summary>The value may be read but must not be written.</summary>
    ReadOnly = 1,

    /// <summary>The value may be read and written.</summary>
    ReadWrite = 2
}

/// <summary>
/// The result of authorizing a caller for one resource. It carries everything an owner needs to
/// enforce AccessControl policy without knowing any AccessControl rule: whether the capability was
/// granted, how to narrow its query, and what each requested field may do.
/// </summary>
public sealed class RecordAccessAuthorization
{
    internal RecordAccessAuthorization(
        bool isAllowed,
        string code,
        TrustedWorkspaceContext? trustedWorkspace,
        RecordAccessScopeFilter scopeFilter,
        string? scopeOwnerMemberId,
        string evaluatedScope,
        IReadOnlyDictionary<string, RecordFieldEnforcement> fieldEnforcement,
        IReadOnlyList<string> unenforceableFieldKeys,
        IReadOnlyList<string> capabilities,
        string policyFingerprint,
        string resourceKey,
        string requiredCapability)
    {
        IsAllowed = isAllowed;
        Code = code;
        TrustedWorkspace = trustedWorkspace;
        ScopeFilter = scopeFilter;
        ScopeOwnerMemberId = scopeOwnerMemberId;
        EvaluatedScope = evaluatedScope;
        FieldEnforcement = fieldEnforcement;
        UnenforceableFieldKeys = unenforceableFieldKeys;
        Capabilities = capabilities;
        PolicyFingerprint = policyFingerprint;
        ResourceKey = resourceKey;
        RequiredCapability = requiredCapability;
    }

    /// <summary>Whether the membership holds the required capability. Record scope is additional to this and can never restore it.</summary>
    public bool IsAllowed { get; }

    /// <summary>`AUTHORIZED`, `ACCESS_DENIED` or `WORKSPACE_MISMATCH`.</summary>
    public string Code { get; }

    public TrustedWorkspaceContext? TrustedWorkspace { get; }

    public RecordAccessScopeFilter ScopeFilter { get; }

    /// <summary>The member a record must be owned by when <see cref="ScopeFilter"/> is <see cref="RecordAccessScopeFilter.OwnedByMember"/>.</summary>
    public string? ScopeOwnerMemberId { get; }

    /// <summary>The wire scope value that was evaluated, for audit and for the record-access projection.</summary>
    public string EvaluatedScope { get; }

    /// <summary>Enforcement for each field the caller asked about, keyed by the owner's canonical field key.</summary>
    public IReadOnlyDictionary<string, RecordFieldEnforcement> FieldEnforcement { get; }

    /// <summary>
    /// Field keys carrying a restrictive policy the owner cannot honour - either the owner does not
    /// declare the field at all, or the field is required by the wire contract and has no admitted
    /// withheld or masked representation. A non-empty list means the operation must fail closed
    /// rather than return a representation the policy forbids.
    /// </summary>
    public IReadOnlyList<string> UnenforceableFieldKeys { get; }

    /// <summary>The membership's effective capabilities, so an owner can test a second capability it already authorized for.</summary>
    public IReadOnlyList<string> Capabilities { get; }

    /// <summary>A deterministic digest of the effective policy this decision was taken against.</summary>
    public string PolicyFingerprint { get; }

    public string ResourceKey { get; }

    public string RequiredCapability { get; }

    public bool Holds(string capability) =>
        capability.Length != 0 && Capabilities.Contains(capability, StringComparer.Ordinal);

    /// <summary>Whether the caller may read this field's value at all.</summary>
    public bool CanRead(string fieldKey) => Enforcement(fieldKey) != RecordFieldEnforcement.Withheld;

    /// <summary>Whether the caller may change this field's value.</summary>
    public bool CanWrite(string fieldKey) => Enforcement(fieldKey) == RecordFieldEnforcement.ReadWrite;

    private RecordFieldEnforcement Enforcement(string fieldKey) =>
        FieldEnforcement.TryGetValue(fieldKey, out var value) ? value : RecordFieldEnforcement.ReadWrite;
}

/// <summary>The record-level half of the decision, taken against authoritative owner facts.</summary>
public sealed record RecordAccessRecordDecision(bool IsAllowed, string EvaluatedScope, bool? OwnerMatch);

/// <summary>
/// The internal AccessControl application boundary a business owner enforces against. It is the
/// same authority the public `POST /access/records/evaluate` operation uses, so a consumer's
/// evaluation and the server's enforcement can never diverge, and no owner reimplements a scope or
/// field rule of its own.
///
/// <para>An owner calls <see cref="AuthorizeResourceAsync"/> once per request - that performs the
/// capability authorization and writes its evidence - and then, once it has loaded the record it
/// already owns, calls <see cref="AuthorizeRecordAsync"/> with its own authoritative facts. Facts
/// supplied by the owner are trusted because the owner is authoritative for them; facts arriving in
/// an HTTP request never are.</para>
/// </summary>
public interface IRecordAccessEvaluator
{
    Task<RecordAccessAuthorization> AuthorizeResourceAsync(
        string resourceKey,
        string requiredCapability,
        IReadOnlyList<string>? requestedFields,
        RecordAccessRequestContext requestContext,
        CancellationToken cancellationToken);

    Task<RecordAccessRecordDecision> AuthorizeRecordAsync(
        RecordAccessAuthorization authorization,
        string recordId,
        RecordAccessFacts facts,
        string enforcementPoint,
        RecordAccessRequestContext requestContext,
        CancellationToken cancellationToken);
}
