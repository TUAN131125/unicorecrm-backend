using System.Text.RegularExpressions;
using UnicoreCRM.Billing.Invoices.Application.Common;
using UnicoreCRM.Billing.Invoices.Contracts;

namespace UnicoreCRM.Billing.Invoices.Application.GetInvoice;

internal sealed record Query(string InvoiceId, InvoiceRequestMetadata Metadata);

/// <summary>
/// The admitted <c>getInvoice</c> operation. Capability authorization runs before the path
/// identifier is inspected, so a caller without <c>invoices.read</c> cannot tell a malformed
/// identifier from a well-formed one, and an identifier outside the Workspace is indistinguishable
/// from one that does not exist.
/// </summary>
internal sealed partial class Handler(InvoiceAuthorization authorization, IInvoicesPersistence persistence, TimeProvider timeProvider)
{
    internal async Task<InvoiceOperationResult<InvoiceDocument>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeInvoicesAsync(query.Metadata, cancellationToken);
        if (!access.IsSuccess)
            return InvoiceOperationResult<InvoiceDocument>.Failure(access.Error!);
        if (!EntityIdPattern().IsMatch(query.InvoiceId))
            return InvoiceOperationResult<InvoiceDocument>.Failure(InvoiceErrors.NotFound());

        var invoice = await persistence.ReadInvoiceAsync(
            access.Value!.Trusted.WorkspaceId,
            query.InvoiceId,
            cancellationToken);
        if (invoice is null)
            return InvoiceOperationResult<InvoiceDocument>.Failure(InvoiceErrors.NotFound());

        var denied = await authorization.EnforceRecordAsync(
            access.Value,
            invoice.InvoiceId,
            "getInvoice",
            query.Metadata,
            cancellationToken);
        if (denied is not null)
            return InvoiceOperationResult<InvoiceDocument>.Failure(denied);

        var document = InvoiceFieldSecurity.Project(InvoiceProjection.Document(invoice), access.Value.Authorization);
        await InvoiceReadAudit.RecordResourceAsync(
            persistence, access.Value, query.Metadata, "getInvoice",
            invoice.InvoiceId, invoice.ResourceVersion, timeProvider.GetUtcNow(), cancellationToken);
        return InvoiceOperationResult<InvoiceDocument>.Success(document);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
