using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;

namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;

internal sealed class DealQuoteCommitmentBinding(QuotesDbContext db) : IDealQuoteCommitmentBinding
{
    public async Task<bool> VerifyAsync(OwnerTransaction transaction, Contracts.DealCommitmentBinding source, CancellationToken ct)
    {
        db.Database.SetDbConnection(transaction.Connection, false);
        await db.Database.UseTransactionAsync(transaction.Transaction, ct);
        var quote = await db.Quotes.AsNoTracking().SingleOrDefaultAsync(q => q.WorkspaceId == source.WorkspaceId && q.QuoteId == source.QuoteId, ct);
        var acceptance = await db.Set<QuoteSalesAcceptance>().AsNoTracking().SingleOrDefaultAsync(a =>
            a.WorkspaceId == source.WorkspaceId && a.QuoteId == source.QuoteId && a.AcceptanceId == source.AcceptanceId, ct);
        if (quote is null || acceptance is null || quote.Status != "ACCEPTED" || quote.ArchivedAt is not null
            || quote.SourceDealId != source.DealId || quote.BuyerType != source.BuyerType || quote.BuyerId != source.BuyerId
            || acceptance.ConversionStatus != "COMPLETED" || acceptance.OrderId != source.OrderId
            || acceptance.Source is not ("CUSTOMER_LINK" or "STAFF_RECORDED")
            || acceptance.CommercialFingerprint != source.CommercialFingerprint
            || QuoteApprovalAssessment.ContentFingerprint(quote) != source.CommercialFingerprint) return false;
        var document = await db.Set<QuoteDocument>().AsNoTracking().SingleOrDefaultAsync(d =>
            d.WorkspaceId == source.WorkspaceId && d.QuoteId == source.QuoteId && d.DocumentId == acceptance.DocumentId, ct);
        if (document is null || document.QuoteRevision != quote.QuoteRevision || document.CommercialFingerprint != source.CommercialFingerprint) return false;
        try
        {
            using var snapshot = JsonDocument.Parse(acceptance.SnapshotJson);
            using var orderAcceptance = JsonDocument.Parse(source.AcceptanceJson);
            var root = snapshot.RootElement;
            var evidence = orderAcceptance.RootElement;
            return root.GetProperty("resourceVersion").GetInt64() == source.SourceQuoteVersion
                && root.GetProperty("sourceDealId").GetString() == source.DealId
                && root.GetProperty("buyerRef").GetProperty("type").GetString() == source.BuyerType
                && root.GetProperty("buyerRef").GetProperty("id").GetString() == source.BuyerId
                && evidence.GetProperty("acceptanceId").GetString() == acceptance.AcceptanceId
                && evidence.GetProperty("quoteId").GetString() == quote.QuoteId
                && evidence.GetProperty("source").GetString() == acceptance.Source
                && evidence.GetProperty("assuranceLevel").GetString() == acceptance.AssuranceLevel;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException) { return false; }
    }
}
