using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Contracts;

/// <summary>
/// Whether the business owner holds a record for the requested identifier inside the trusted
/// Workspace. A record that belongs to a different Workspace is <see cref="NotFound"/>: the owner
/// never reports foreign-Workspace existence, so AccessControl cannot leak it either.
/// </summary>
public enum RecordAccessFactStatus
{
    NotFound = 0,
    Found = 1
}

/// <summary>
/// The authoritative record facts AccessControl needs to evaluate record scope. It carries no
/// business field values: only existence inside the trusted Workspace and the owner member
/// reference the scope rules are defined against.
/// </summary>
/// <param name="OwnerMemberId">
/// The Workspace member the record is owned by, when the owner records one. A record with no
/// owner reference fails <c>OWN</c> scope closed.
/// </param>
public sealed record RecordAccessFacts(RecordAccessFactStatus Status, string? OwnerMemberId = null)
{
    public static RecordAccessFacts NotFound { get; } = new(RecordAccessFactStatus.NotFound);
    public static RecordAccessFacts Found(string? ownerMemberId) => new(RecordAccessFactStatus.Found, ownerMemberId);
}

/// <summary>
/// The canonical resource and capability vocabulary a business owner declares for record-access
/// evaluation. The owner is authoritative for its own resource key, capability names and command
/// vocabulary; AccessControl is authoritative for whether the caller holds those capabilities.
/// A capability left null has no admitted operation behind it, so the matching action is denied.
/// </summary>
public sealed class RecordAccessResourceDescriptor
{
    private RecordAccessResourceDescriptor(
        string resourceKey,
        string readCapability,
        string? updateCapability,
        string? deleteCapability,
        string? exportCapability,
        string? approveCapability,
        IReadOnlyDictionary<string, string> commandCapabilities,
        IReadOnlyDictionary<string, bool> enforceableFields)
    {
        ResourceKey = resourceKey;
        ReadCapability = readCapability;
        UpdateCapability = updateCapability;
        DeleteCapability = deleteCapability;
        ExportCapability = exportCapability;
        ApproveCapability = approveCapability;
        CommandCapabilities = commandCapabilities;
        EnforceableFields = enforceableFields;
    }

    public string ResourceKey { get; }
    public string ReadCapability { get; }
    public string? UpdateCapability { get; }
    public string? DeleteCapability { get; }
    public string? ExportCapability { get; }
    public string? ApproveCapability { get; }

    /// <summary>Command name to the canonical capability that command requires.</summary>
    public IReadOnlyDictionary<string, string> CommandCapabilities { get; }

    /// <summary>
    /// The field keys this owner can actually enforce a field-security policy on, mapped to whether
    /// the owner's full read model makes the field required.
    ///
    /// <para>A field absent from this vocabulary is neither readable nor writable: it fails closed
    /// rather than defaulting permissive, and the public evaluation reports it HIDDEN. It does not
    /// refuse the operation, because the owner never projects it and so has nothing to withhold.</para>
    ///
    /// <para>A field present here that the representation being returned makes required cannot carry
    /// a restrictive policy at all, because no admitted absent or masked representation exists for
    /// it. That case fails the operation closed rather than returning the forbidden value. See
    /// <see cref="RecordAccessRepresentation"/> for why required-ness is per representation.</para>
    /// </summary>
    public IReadOnlyDictionary<string, bool> EnforceableFields { get; }

    public static RecordAccessResourceDescriptor Create(
        string resourceKey,
        string readCapability,
        string? updateCapability = null,
        string? deleteCapability = null,
        string? exportCapability = null,
        string? approveCapability = null,
        IReadOnlyDictionary<string, string>? commandCapabilities = null,
        IReadOnlyDictionary<string, bool>? enforceableFields = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        if (resourceKey.Trim().Length is < 1 or > 160)
            throw new ArgumentException("A resource key must contain between 1 and 160 characters.", nameof(resourceKey));

        var commands = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in commandCapabilities ?? new Dictionary<string, string>(StringComparer.Ordinal))
        {
            if (pair.Key.Length is < 1 or > 160)
                throw new ArgumentException("A command name must contain between 1 and 160 characters.", nameof(commandCapabilities));
            commands[pair.Key] = Canonical(pair.Value, nameof(commandCapabilities));
        }

        var fields = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in enforceableFields ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase))
        {
            if (pair.Key.Length is < 1 or > 160)
                throw new ArgumentException("A field key must contain between 1 and 160 characters.", nameof(enforceableFields));
            fields[pair.Key] = pair.Value;
        }

        return new RecordAccessResourceDescriptor(
            resourceKey.Trim(),
            Canonical(readCapability, nameof(readCapability)),
            OptionalCanonical(updateCapability, nameof(updateCapability)),
            OptionalCanonical(deleteCapability, nameof(deleteCapability)),
            OptionalCanonical(exportCapability, nameof(exportCapability)),
            OptionalCanonical(approveCapability, nameof(approveCapability)),
            commands,
            fields);
    }

    private static string Canonical(string capability, string parameterName)
    {
        try
        {
            return AccessRequirement.ForCanonicalCapability(capability).Capability;
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException("A record-access descriptor must use canonical capability identifiers.", parameterName, exception);
        }
    }

    private static string? OptionalCanonical(string? capability, string parameterName) =>
        string.IsNullOrWhiteSpace(capability) ? null : Canonical(capability, parameterName);
}

/// <summary>
/// Which representation an operation is about to return, for the sole purpose of deciding whether a
/// restrictive field policy can be honoured by omitting the value or must fail the operation closed.
/// Required-ness is a property of the representation, not of the resource: the full read model of a
/// resource can make a field required while a minimized projection of the same resource declares it
/// optional, and a value that has an admitted absent representation must be withheld rather than
/// refused.
///
/// <para><b>Why this cannot widen access.</b> A representation is consulted at exactly one place -
/// whether a field belongs in <see cref="RecordAccessAuthorization.UnenforceableFieldKeys"/>. It
/// never reaches <c>CanRead</c>, <c>CanWrite</c> or the wire projection. The strongest thing an
/// operation can achieve by declaring a field optional is to turn "refuse the whole operation" into
/// "withhold this value". It can never turn a withheld value into a returned one, so a false
/// declaration cannot disclose anything.</para>
///
/// <para>A key the owner does not declare in its enforceable vocabulary is ignored, so an operation
/// cannot relax a policy on a field the owner never admitted in the first place. Instances are
/// intended to be <c>static readonly</c> on the operation that owns the representation, not built
/// per request from caller input.</para>
/// </summary>
public sealed class RecordAccessRepresentation
{
    private RecordAccessRepresentation(string name, IReadOnlySet<string> optionalFieldKeys)
    {
        Name = name;
        OptionalFieldKeys = optionalFieldKeys;
    }

    /// <summary>The resource's own declaration governs: no field is treated as optional beyond what the owner declared.</summary>
    public static RecordAccessRepresentation Full { get; } =
        new("full", new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    public string Name { get; }

    /// <summary>Fields this representation can return absent, whatever the full read model declares.</summary>
    public IReadOnlySet<string> OptionalFieldKeys { get; }

    /// <param name="name">A stable identifier for the representation, for evidence and diagnostics.</param>
    /// <param name="optionalFieldKeys">
    /// The fields this representation declares optional. Every one must genuinely be optional in the
    /// contract this operation returns; declaring otherwise cannot disclose a value, but it would
    /// misreport why an operation refused.
    /// </param>
    public static RecordAccessRepresentation Create(string name, params string[] optionalFieldKeys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(optionalFieldKeys);
        if (name.Trim().Length is < 1 or > 160)
            throw new ArgumentException("A representation name must contain between 1 and 160 characters.", nameof(name));

        // The canonical AccessControl key rule. It cannot diverge dangerously even if it were
        // changed here by mistake: CanOmit first requires the descriptor to declare the field, and
        // that lookup uses the canonical comparer, so a mismatch can only fail closed.
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fieldKey in optionalFieldKeys)
        {
            if (string.IsNullOrWhiteSpace(fieldKey) || fieldKey.Length > 160)
                throw new ArgumentException("A representation field key must contain between 1 and 160 characters.", nameof(optionalFieldKeys));
            keys.Add(fieldKey);
        }

        return new RecordAccessRepresentation(name.Trim(), keys);
    }

    /// <summary>
    /// Whether this representation can return the field absent. A key the owner does not declare is
    /// not honoured, so an operation cannot relax a policy on a field outside the owner vocabulary.
    /// </summary>
    internal bool CanOmit(RecordAccessResourceDescriptor? descriptor, string fieldKey) =>
        descriptor is not null
        && descriptor.EnforceableFields.ContainsKey(fieldKey)
        && OptionalFieldKeys.Contains(fieldKey);
}

public sealed record RecordAccessRequestContext(string RequestId, string CorrelationId);

/// <summary>
/// The narrow owner-owned boundary AccessControl uses to obtain authoritative record facts. The
/// business owner implements it over its own persistence; AccessControl never reads a foreign
/// DbContext, repository, Infrastructure type or EF entity.
///
/// An implementation must query only the supplied trusted Workspace, must not run its own
/// capability authorization (AccessControl has already authorized the caller before calling it,
/// and a second authority would duplicate the decision), and must not mutate business state.
/// </summary>
public interface IRecordAccessFactProvider
{
    RecordAccessResourceDescriptor Descriptor { get; }

    Task<RecordAccessFacts> ReadFactsAsync(
        TrustedWorkspaceContext trustedWorkspace,
        string recordId,
        RecordAccessRequestContext requestContext,
        CancellationToken cancellationToken);
}
