namespace UnicoreCRM.Sales.Quotes.Application.Common;

/// <summary>Negative-only implementation of the admitted workflow boundary until a consent verifier exists.</summary>
internal sealed class QuoteAcceptanceGuard(QuoteAuthorization authorization, IQuotesPersistence persistence)
{
    internal async Task<QuoteOperationError> CheckAsync(string quoteId, QuoteCommandMetadata command, CancellationToken ct)
    {
        var metadata = new QuoteRequestMetadata(command.RequestId, command.CorrelationId);
        var read = await authorization.AuthorizeAsync(metadata, ct);
        if (!read.IsSuccess) return read.Error!;
        var access = await authorization.AuthorizeAsync(metadata, ct, Contracts.QuoteCapabilities.Update);
        if (!access.IsSuccess) return access.Error!;
        if (!QuotePricing.EntityId(quoteId)) return QuoteErrors.NotFound();
        var quote = await persistence.ReadQuoteAsync(access.Value!.Trusted.WorkspaceId, quoteId, ct);
        if (quote is null) return QuoteErrors.NotFound();
        foreach (var grant in new[] { read.Value!, access.Value })
        {
            var denied = await authorization.EnforceRecordAsync(grant, quote, "acceptQuoteAndCloseDeal", metadata, ct);
            if (denied is not null) return denied;
        }
        if (QuoteDeliveryCommands.ReadFields.Any(field => !access.Value.Authorization.CanRead(field) || !read.Value!.Authorization.CanRead(field)))
            return QuoteErrors.AccessDenied();
        if (quote.ResourceVersion != command.ExpectedVersion) return new("VERSION_CONFLICT", 412, "Resource version conflict");
        // Never replay legacy/unverified acceptance receipts as trusted customer commitments.
        if (quote.Status != "SENT" || quote.ArchivedAt is not null)
            return new("QUOTE_ACCEPTANCE_BLOCKED", 409, "Quote cannot be accepted", "QUOTE_STATE_INVALID");
        return new("QUOTE_ACCEPTANCE_BLOCKED", 409, "Verified customer acceptance is unavailable",
            quote.SourceDealId is null ? "CUSTOMER_EVIDENCE_VERIFIER_UNAVAILABLE" : "ATOMIC_ACCEPTANCE_CONTRACT_UNAVAILABLE");
    }
}
