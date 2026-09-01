using UnicoreCRM.Billing.Invoices.Application.Common;
using UnicoreCRM.Billing.Invoices.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Billing.Invoices.Application.ListInvoices;

internal sealed record Query(InvoiceRequestMetadata Metadata);

/// <summary>
/// The admitted <c>listInvoices</c> operation. Current authority admits no path or query parameter
/// for it, so no filter, search, sort, ordering guarantee, cursor or pagination is implemented.
/// </summary>
internal sealed class Handler(InvoiceAuthorization authorization, IInvoicesPersistence persistence, TimeProvider timeProvider)
{
    internal async Task<InvoiceOperationResult<IReadOnlyList<InvoiceDocument>>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeInvoicesAsync(query.Metadata, cancellationToken);
        if (!access.IsSuccess)
            return InvoiceOperationResult<IReadOnlyList<InvoiceDocument>>.Failure(access.Error!);

        // WORKSPACE is the only admitted data scope for this operation. OWN, TEAM and CUSTOM have no
        // authoritative Invoice ownership or team fact behind them and disclose nothing.
        InvoiceDocument[] documents = [];
        if (access.Value!.Authorization.ScopeFilter == RecordAccessScopeFilter.Workspace)
        {
            var invoices = await persistence.ReadInvoicesAsync(access.Value.Trusted.WorkspaceId, cancellationToken);
            documents = invoices
                .Select(invoice => InvoiceFieldSecurity.Project(InvoiceProjection.Document(invoice), access.Value.Authorization))
                .ToArray();
        }

        // One row per successful invocation, never one per returned Invoice, and written only after
        // every projection has succeeded. An empty successful result still writes it.
        await InvoiceReadAudit.RecordListAsync(
            persistence, access.Value, query.Metadata, "listInvoices", timeProvider.GetUtcNow(), cancellationToken);
        return InvoiceOperationResult<IReadOnlyList<InvoiceDocument>>.Success(documents);
    }
}
