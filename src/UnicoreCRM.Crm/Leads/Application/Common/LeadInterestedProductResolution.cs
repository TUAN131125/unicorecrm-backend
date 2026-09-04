using UnicoreCRM.Crm.Leads.Domain;
using UnicoreCRM.Sales.Products.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.Common;

/// <summary>
/// Turns caller-supplied interested-product intents into Leads-owned historical snapshots, using the
/// narrow Products-owned reader for every identifier the Lead does not already carry.
///
/// Leads never opens <c>ProductsDbContext</c> and never interprets Product lifecycle or status:
/// resolution and eligibility are decided by Products and consumed here as a verdict.
/// </summary>
internal sealed class LeadInterestedProductResolution(
    IProductSnapshotReader products,
    TimeProvider timeProvider)
{
    internal sealed record Outcome(IReadOnlyList<LeadInterestedProduct>? Items, LeadOperationError? Error);

    /// <summary>
    /// Create: every entry is a new capture, so every identifier is resolved.
    /// </summary>
    internal Task<Outcome> ResolveForCreateAsync(
        IReadOnlyList<LeadInterestedProductIntent> intents,
        CancellationToken cancellationToken) =>
        ResolveAsync(intents, [], cancellationToken);

    /// <summary>
    /// Replace: the submitted collection is the desired state. An identifier the Lead already carries
    /// keeps its captured snapshot untouched and takes only the caller's own field changes; an
    /// identifier the Lead did not carry is a new capture; an identifier no longer submitted is
    /// dropped.
    ///
    /// A retained entry is deliberately never revalidated. Editing an unrelated Lead field must not
    /// fail because a Product was archived after capture, and must not silently refresh a captured
    /// name - that would be exactly the rehydration the snapshot rule forbids.
    /// </summary>
    internal Task<Outcome> ResolveForReplaceAsync(
        IReadOnlyList<LeadInterestedProductIntent> intents,
        IReadOnlyList<LeadInterestedProduct> existing,
        CancellationToken cancellationToken) =>
        ResolveAsync(intents, existing, cancellationToken);

    private async Task<Outcome> ResolveAsync(
        IReadOnlyList<LeadInterestedProductIntent> intents,
        IReadOnlyList<LeadInterestedProduct> existing,
        CancellationToken cancellationToken)
    {
        if (intents.Count == 0)
            return new Outcome([], null);

        var retained = existing.ToDictionary(item => item.ProductId, StringComparer.Ordinal);
        var toResolve = intents
            .Where(intent => !retained.ContainsKey(intent.ProductId))
            .Select(intent => intent.ProductId)
            .ToArray();

        IReadOnlyDictionary<string, ProductSnapshotEntry> resolved =
            new Dictionary<string, ProductSnapshotEntry>(StringComparer.Ordinal);
        if (toResolve.Length != 0)
        {
            // One batch read for the whole command, so every fact in a single Lead write comes from
            // one consistent read pass rather than N independently timed reads.
            var read = await products.ResolveAsync(toResolve, cancellationToken);
            if (!read.IsAuthorized)
            {
                // products.read was refused. No Product fact and no per-identifier outcome is
                // disclosed - not even whether the supplied identifiers exist.
                return new Outcome(null, LeadErrors.AccessDenied());
            }

            resolved = read.Entries.ToDictionary(entry => entry.ProductId, StringComparer.Ordinal);
        }

        var now = timeProvider.GetUtcNow();
        var items = new List<LeadInterestedProduct>(intents.Count);
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);

        for (var index = 0; index < intents.Count; index++)
        {
            var intent = intents[index];
            if (retained.TryGetValue(intent.ProductId, out var stored))
            {
                items.Add(stored with
                {
                    InterestLevel = intent.InterestLevel,
                    EstimatedQuantity = intent.EstimatedQuantity,
                    ExpectedBudget = intent.ExpectedBudget,
                    Note = intent.Note
                });
                continue;
            }

            var field = $"interestedProducts[{index}].productId";
            if (!resolved.TryGetValue(intent.ProductId, out var entry))
            {
                fields[field] = [UnresolvableMessage];
                continue;
            }

            switch (entry.Outcome)
            {
                case ProductSnapshotOutcome.Resolved when entry.Facts is { } facts:
                    items.Add(new LeadInterestedProduct(
                        LeadIds.New("leadproduct"),
                        facts.ProductId,
                        facts.Name,
                        intent.InterestLevel,
                        intent.EstimatedQuantity,
                        intent.ExpectedBudget,
                        intent.Note,
                        now)
                    {
                        SkuSnapshot = facts.Sku,
                        ProductTypeSnapshot = facts.ProductType,
                        ProductVersionSnapshot = facts.Version
                    });
                    break;

                case ProductSnapshotOutcome.NotEligible:
                    fields[field] = [IneligibleMessage];
                    break;

                default:
                    // Unknown, foreign-Workspace and structurally invalid all arrive here with the
                    // same verdict and produce the same message, so the response can never reveal
                    // that a Product exists in another Workspace.
                    fields[field] = [UnresolvableMessage];
                    break;
            }
        }

        // All-or-nothing: one bad entry fails the whole Lead command, and no valid entry is
        // partially committed or silently dropped.
        return fields.Count == 0
            ? new Outcome(items, null)
            : new Outcome(null, LeadErrors.Validation(fields));
    }

    private const string UnresolvableMessage =
        "productId must reference a Product of the trusted workspace.";

    private const string IneligibleMessage =
        "productId must reference an active Product.";
}
