using UnicoreCRM.Platform.AccessControl.Contracts;

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

    internal static IReadOnlyList<string> Capabilities { get; } =
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
        "tasks.assign",
        "tasks.complete",
        "tasks.create",
        "tasks.read",
        "tasks.update",
        "workspace.context.resolve"
    ];

    /// <summary>Fails closed if the frozen set ever drifts from the canonical capability contract.</summary>
    internal static IReadOnlyList<string> Validated()
    {
        var validated = Capabilities
            .Select(AccessRequirement.ForCanonicalCapability)
            .Select(requirement => requirement.Capability)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (validated.Length != Capabilities.Count)
            throw new InvalidOperationException("The initial Workspace access capability set is not canonical.");
        return validated;
    }
}
