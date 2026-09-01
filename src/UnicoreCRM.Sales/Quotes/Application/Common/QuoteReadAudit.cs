using UnicoreCRM.Sales.Quotes.Domain;

namespace UnicoreCRM.Sales.Quotes.Application.Common;

/// <summary>
/// Persists one Quotes-owned disclosure record after projection and before success is returned.
/// </summary>
internal static class QuoteReadAudit
{
    internal static Task RecordListAsync(
        IQuotesPersistence persistence,
        QuoteAccess access,
        QuoteRequestMetadata metadata,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        AppendAsync(persistence, access, metadata, "listQuotes", null, null, occurredAt, cancellationToken);

    internal static Task RecordResourceAsync(
        IQuotesPersistence persistence,
        QuoteAccess access,
        QuoteRequestMetadata metadata,
        string recordId,
        long resourceVersion,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        AppendAsync(
            persistence,
            access,
            metadata,
            "getQuote",
            recordId,
            resourceVersion,
            occurredAt,
            cancellationToken);

    private static async Task AppendAsync(
        IQuotesPersistence persistence,
        QuoteAccess access,
        QuoteRequestMetadata metadata,
        string operation,
        string? recordId,
        long? resourceVersion,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        persistence.AddReadAudit(new QuoteReadAuditRecord(
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
