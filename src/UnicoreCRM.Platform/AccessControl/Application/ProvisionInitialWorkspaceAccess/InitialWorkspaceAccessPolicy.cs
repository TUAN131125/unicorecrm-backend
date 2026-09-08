using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.AccessControl.Application.Common;
using UnicoreCRM.Platform.AccessControl.Domain;

namespace UnicoreCRM.Platform.AccessControl.Application.ProvisionInitialWorkspaceAccess;

/// <summary>
/// The server-owned definition of the single initial access assignment created for the account
/// that provisions its own first Workspace. The capability set contains only canonical
/// capabilities that current implementation authority already admits for implemented
/// operations. Access administration and Studio configuration are included because their
/// current operations are admitted; audit remains excluded because no production-ready audit
/// operation is currently admitted. The caller can neither extend nor replace this set.
/// </summary>
internal static class InitialWorkspaceAccessPolicy
{
    internal const string RoleName = "Workspace Owner";
    internal const string RoleDescription = "Initial Workspace provisioning role for the account that created this Workspace.";
    internal const string SystemOwnerTemplateId = "system:workspace-owner";

    // Frozen historical snapshot. Do not add future capabilities here: a stored role must match
    // this exact previously server-owned set before the Contacts Read Core upgrade is admitted.
    private static IReadOnlyList<string> PreContactsCapabilities { get; } =
    [
        "deals.assign",
        "deals.bulk",
        "deals.close",
        "deals.create",
        "deals.delete",
        "deals.read",
        "deals.update",
        "leads.create",
        "leads.qualify",
        "leads.read",
        "leads.update",
        "products.create",
        "products.delete",
        "products.edit",
        "products.read",
        "support.assign",
        "support.create",
        "support.read",
        "support.update",
        "tasks.assign",
        "tasks.complete",
        "tasks.create",
        "tasks.read",
        "tasks.update",
        "workspace.context.resolve"
    ];

    private static IReadOnlyList<string> RestrictedOwnerCapabilitiesV2 { get; } =
    [
        "contacts.read",
        .. PreContactsCapabilities
    ];

    // Exact owner projection immediately preceding authoritative Contact writes. Deriving it from
    // the current server-owned set keeps unrelated admitted module capabilities intact while still
    // refusing arbitrary subsets or caller-invented capabilities.
    private static IReadOnlyList<string> PreContactWritesOwnerCapabilities { get; } =
        WorkspaceCapabilityPolicy.WorkspaceOwnerCapabilities
            .Where(capability => capability is not "contacts.create" and not "contacts.update" and not "contacts.delete")
            .ToArray();

    internal static IReadOnlyList<string> Capabilities { get; } =
        WorkspaceCapabilityPolicy.WorkspaceOwnerCapabilities;

    /// <summary>Fails closed if the frozen set ever drifts from the canonical capability contract.</summary>
    internal static IReadOnlyList<string> Validated()
        => Validate(Capabilities, "The initial Workspace access capability set is not canonical.");

    /// <summary>
    /// Admits only the exact server-owned snapshot immediately preceding Contacts Read Core.
    /// Arbitrary subsets and sets containing unexpected capabilities remain drift and fail closed.
    /// </summary>
    internal static bool IsKnownPreviousCapabilitySet(IReadOnlyList<string> storedCapabilities)
    {
        var v1 = Validate(PreContactsCapabilities, "The V1 initial Workspace access capability set is not canonical.");
        var v2 = Validate(RestrictedOwnerCapabilitiesV2, "The V2 initial Workspace access capability set is not canonical.");
        var preContactWrites = Validate(PreContactWritesOwnerCapabilities, "The pre-Contact-writes Workspace Owner capability set is not canonical.");
        return storedCapabilities.SequenceEqual(v1, StringComparer.Ordinal)
            || storedCapabilities.SequenceEqual(v2, StringComparer.Ordinal)
            || storedCapabilities.SequenceEqual(preContactWrites, StringComparer.Ordinal);
    }

    /// <summary>
    /// The untouched-seed signature. It is used only on the fallback path taken when this membership
    /// has no AccessControl assignment yet, to decide whether a role already carrying the seeded
    /// display name is the freshly created seed whose assignment write did not land. It is not a
    /// protected-role concept and is deliberately never applied to a role reached through the
    /// assignment anchor: an admitted <c>replaceAccessRole</c> legitimately changes the name,
    /// description, template provenance and version, and that is a committed mutation rather than
    /// provisioning corruption.
    /// </summary>
    internal static bool HasUntouchedSeedIdentity(AccessRole role, string workspaceId) =>
        string.Equals(role.WorkspaceId, workspaceId, StringComparison.Ordinal)
        && string.Equals(role.Name, RoleName, StringComparison.Ordinal)
        && string.Equals(role.Description, RoleDescription, StringComparison.Ordinal)
        && (role.SourceTemplateId is null
            || string.Equals(role.SourceTemplateId, SystemOwnerTemplateId, StringComparison.Ordinal))
        && role.IsActive
        && role.Version == 0;

    private static IReadOnlyList<string> Validate(
        IReadOnlyList<string> capabilities,
        string errorMessage)
    {
        var validated = capabilities
            .Select(AccessRequirement.ForCanonicalCapability)
            .Select(requirement => requirement.Capability)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (validated.Length != capabilities.Count)
            throw new InvalidOperationException(errorMessage);
        return validated;
    }
}
