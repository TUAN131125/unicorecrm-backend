using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.AccessControl.Domain;

namespace UnicoreCRM.Platform.AccessControl.Application.ProvisionInitialWorkspaceAccess;

/// <summary>
/// The server-owned definition of the single initial access assignment created for the account
/// that provisions its own first Workspace. The capability set contains only canonical
/// capabilities that current implementation authority already admits for implemented
/// operations. Capabilities whose administrative operations remain fail-closed - access
/// administration, Studio configuration and audit - are deliberately excluded, and the caller
/// can neither extend nor replace this set.
/// </summary>
internal static class InitialWorkspaceAccessPolicy
{
    internal const string RoleName = "Workspace Owner";
    internal const string RoleDescription = "Initial Workspace provisioning role for the account that created this Workspace.";

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

    internal static IReadOnlyList<string> Capabilities { get; } =
    [
        "contacts.read",
        .. PreContactsCapabilities
    ];

    /// <summary>Fails closed if the frozen set ever drifts from the canonical capability contract.</summary>
    internal static IReadOnlyList<string> Validated()
        => Validate(Capabilities, "The initial Workspace access capability set is not canonical.");

    /// <summary>
    /// Admits only the exact server-owned snapshot immediately preceding Contacts Read Core.
    /// Arbitrary subsets and sets containing unexpected capabilities remain drift and fail closed.
    /// </summary>
    internal static bool IsKnownPreviousCapabilitySet(IReadOnlyList<string> storedCapabilities)
    {
        var previous = Validate(
            PreContactsCapabilities,
            "The previous initial Workspace access capability set is not canonical.");
        return storedCapabilities.SequenceEqual(previous, StringComparer.Ordinal);
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
        && role.SourceTemplateId is null
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
