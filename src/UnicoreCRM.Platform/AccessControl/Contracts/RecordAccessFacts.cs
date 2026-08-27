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
    /// the wire contract makes the field required. A restrictive policy on a field absent from this
    /// vocabulary cannot be honoured at all, and one on a required field cannot be honoured either,
    /// because no admitted representation exists for a required field whose value must not be
    /// exposed. Both cases fail the operation closed rather than returning the forbidden value.
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
