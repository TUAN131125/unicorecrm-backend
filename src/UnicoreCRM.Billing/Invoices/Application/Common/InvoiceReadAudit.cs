using UnicoreCRM.Billing.Invoices.Domain;

namespace UnicoreCRM.Billing.Invoices.Application.Common;

/// <summary>
/// Appends the one Invoices-owned read-evidence row for a successful disclosure.
///
/// <para>Callers must invoke this only after the response representation has been materialized, so
/// a capability denial, a malformed identifier, a record-access denial, an unknown or foreign
/// Invoice, a required hidden field, or contract-invalid persisted state that throws during
/// projection leaves no Invoices evidence. The append and its <c>SaveChangesAsync</c> run on the
/// Invoices context before the handler returns, so a failed append surfaces as a failed request
/// rather than an undocumented disclosure.</para>
/// </summary>
internal static class InvoiceReadAudit
{
    internal static Task RecordListAsync(
        IInvoicesPersistence persistence,
        InvoiceAccess access,
        InvoiceRequestMetadata metadata,
        string operation,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        AppendAsync(persistence, access, metadata, operation, null, null, occurredAt, cancellationToken);

    internal static Task RecordResourceAsync(
        IInvoicesPersistence persistence,
        InvoiceAccess access,
        InvoiceRequestMetadata metadata,
        string operation,
        string recordId,
        long? resourceVersion,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        AppendAsync(persistence, access, metadata, operation, recordId, resourceVersion, occurredAt, cancellationToken);

    private static async Task AppendAsync(
        IInvoicesPersistence persistence,
        InvoiceAccess access,
        InvoiceRequestMetadata metadata,
        string operation,
        string? recordId,
        long? resourceVersion,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        persistence.AddReadAudit(new InvoiceReadAuditRecord(
            operation,
            access.Trusted.WorkspaceId,
            access.Trusted.MemberId,
            recordId,
            metadata.RequestId,
            metadata.CorrelationId,
            resourceVersion,
            occurredAt));
        await persistence.SaveChangesAsync(cancellationToken);
    }
}
