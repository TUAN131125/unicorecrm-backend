using UnicoreCRM.Billing.Payments.Domain;

namespace UnicoreCRM.Billing.Payments.Application.Common;

/// <summary>
/// Appends the one Payments-owned read-evidence row for a successful disclosure.
///
/// <para>Callers must invoke this only after the response representation has been produced, so a
/// capability denial, a malformed identifier, a record-access denial, an unknown or foreign record,
/// or contract-invalid persisted state that throws during projection leaves no Payments evidence.
/// The append and its <c>SaveChangesAsync</c> run on the Payments context before the handler returns,
/// so a failed append surfaces as a failed request rather than an undocumented disclosure.</para>
/// </summary>
internal static class PaymentReadAudit
{
    internal static Task RecordListAsync(
        IPaymentsPersistence persistence,
        PaymentAccess access,
        PaymentRequestMetadata metadata,
        string operation,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        AppendAsync(persistence, access, metadata, operation, null, null, occurredAt, cancellationToken);

    internal static Task RecordResourceAsync(
        IPaymentsPersistence persistence,
        PaymentAccess access,
        PaymentRequestMetadata metadata,
        string operation,
        string recordId,
        long? resourceVersion,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        AppendAsync(persistence, access, metadata, operation, recordId, resourceVersion, occurredAt, cancellationToken);

    private static async Task AppendAsync(
        IPaymentsPersistence persistence,
        PaymentAccess access,
        PaymentRequestMetadata metadata,
        string operation,
        string? recordId,
        long? resourceVersion,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        persistence.AddReadAudit(new PaymentReadAuditRecord(
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
