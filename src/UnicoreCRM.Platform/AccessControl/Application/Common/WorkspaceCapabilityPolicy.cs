namespace UnicoreCRM.Platform.AccessControl.Application.Common;

/// <summary>
/// Server-owned projection of the Workspace-scoped capabilities required by operations that are
/// currently production-contract ready. Global identity/session capabilities and blocked or
/// deferred product capabilities are deliberately absent.
/// </summary>
internal static class WorkspaceCapabilityPolicy
{
    internal static IReadOnlyList<string> WorkspaceOwnerCapabilities { get; } =
    [
        "access.configure",
        "access.read",
        "contacts.create",
        "contacts.read",
        "contacts.update",
        "customers.view",
        "deals.assign",
        "deals.bulk",
        "deals.close",
        "deals.create",
        "deals.delete",
        "deals.read",
        "deals.update",
        "invoices.create",
        "invoices.create_credit_note",
        "invoices.edit",
        "invoices.issue",
        "invoices.read",
        "invoices.send",
        "invoices.update_draft",
        "invoices.void",
        "leads.assign",
        "leads.bulk",
        "leads.create",
        "leads.delete",
        "leads.export",
        "leads.qualify",
        "leads.read",
        "leads.update",
        "orders.complete",
        "orders.confirm",
        "orders.create",
        "orders.credit_approval.decide",
        "orders.credit_approval.request",
        "orders.delete",
        "orders.read",
        "orders.update",
        "organizations.read",
        "payments.allocate",
        "payments.intent.cancel",
        "payments.intent.create",
        "payments.plan.activate",
        "payments.plan.read",
        "payments.plan.supersede",
        "payments.plan.update_draft",
        "payments.read",
        "payments.reconcile",
        "payments.record_manual",
        "payments.refund",
        "payments.reverse_allocation",
        "products.create",
        "products.delete",
        "products.edit",
        "products.read",
        "quotes.approve",
        "quotes.create",
        "quotes.delete",
        "quotes.read",
        "quotes.update",
        "receivables.read",
        "returns.read",
        "returns.resolve",
        "returns.update",
        "shipping.create",
        "shipping.read",
        "studio.configure",
        "studio.read",
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

    // At the current contract baseline every admitted Workspace capability is assignable by an
    // authorized administrator. This remains a separate projection so future owner-only authority
    // does not have to leak into ordinary custom roles.
    internal static IReadOnlyList<string> CustomRoleAssignableCapabilities { get; } =
        WorkspaceOwnerCapabilities;
}
