using System.Text.RegularExpressions;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Contracts;

public sealed partial class AccessRequirement
{
    private AccessRequirement(string capability) => Capability = capability;

    public string Capability { get; }

    public static AccessRequirement ForCanonicalCapability(string capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        if (!CapabilityPattern().IsMatch(capability))
            throw new ArgumentException("A capability must be a canonical identifier of at most 160 characters.", nameof(capability));
        return new AccessRequirement(capability);
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.:-]{0,159}$", RegexOptions.CultureInvariant)]
    private static partial Regex CapabilityPattern();
}

public static class AccessCapabilities
{
    public static AccessRequirement WorkspaceContextResolve { get; } = AccessRequirement.ForCanonicalCapability("workspace.context.resolve");
    public static AccessRequirement AccessRead { get; } = AccessRequirement.ForCanonicalCapability("access.read");
    public static AccessRequirement AccessConfigure { get; } = AccessRequirement.ForCanonicalCapability("access.configure");
}

public sealed record AuthorizationContextDocument(
    string WorkspaceId,
    string MembershipId,
    string MemberId,
    string AccountId,
    IReadOnlyList<string> RoleIds,
    IReadOnlyList<string> RoleTemplateIds,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> ProductSpaces,
    IReadOnlyList<AuthorizationDataScopeEntry> DataScopes,
    IReadOnlyList<AuthorizationFieldAccessEntry> FieldSecurity,
    DateTimeOffset EvaluatedAt);

public sealed record AuthorizationDataScopeEntry(string ResourceKey, string Scope);
public sealed record AuthorizationFieldAccessEntry(string ResourceKey, string FieldKey, string Access);

public sealed record AccessAuthorizationDecision(
    bool IsAllowed,
    string Code,
    AuthorizationContextDocument? Context);

public interface IAccessAuthorizer
{
    Task<AccessAuthorizationDecision> AuthorizeAsync(
        AccessRequirement requirement,
        string correlationId,
        CancellationToken cancellationToken);
}

public interface IDelegatedAccessAuthorizer
{
    Task<AccessAuthorizationDecision> AuthorizeAsync(
        TrustedWorkspaceContext trustedWorkspace,
        AccessRequirement requirement,
        string correlationId,
        CancellationToken cancellationToken);
}

public interface ICurrentAuthorizationContext
{
    bool IsResolved { get; }
    AuthorizationContextDocument Require();
}

public sealed record AccessProblemDetails(
    string Type,
    string Title,
    int Status,
    string Code,
    bool Retryable,
    string CorrelationId,
    string? Detail = null,
    string? Instance = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null,
    string? IdempotencyKey = null);
