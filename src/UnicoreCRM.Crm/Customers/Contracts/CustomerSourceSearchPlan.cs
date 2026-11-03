namespace UnicoreCRM.Crm.Customers.Contracts;

/// <summary>
/// Internal read-side ID projection, authored by the source owner after authorization.
/// SQL is fixed owner code, never client input. It exports only SourceId, with source
/// Workspace/record scope and readable-field predicates already applied. The Customer
/// owner composes this ID set into its own bounded, cursor-ordered read query.
/// </summary>
internal sealed record CustomerSourceSearchPlan(string Sql, IReadOnlyDictionary<string, object?> Parameters);
