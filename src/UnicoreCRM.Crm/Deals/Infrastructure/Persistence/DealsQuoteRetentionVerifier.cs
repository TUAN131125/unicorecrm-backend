using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;
namespace UnicoreCRM.Crm.Deals.Infrastructure.Persistence;

internal sealed class DealsQuoteRetentionVerifier(DealsDbContext db, IRecordAccessEvaluator access,
    IEnumerable<IRecordAccessFactProvider> facts, ILogger<DealsQuoteRetentionVerifier> logger) : IQuoteRetentionVerifier
{
    public QuoteRetentionOwner Owner => QuoteRetentionOwner.Deals;
    public async Task<IQuoteRetentionLease> AcquireAsync(string quoteId, string requestId, string correlationId, CancellationToken ct)
    {
        var providers = facts.Where(p => p.Descriptor.ResourceKey == "quotes").ToArray();
        if (providers.Length != 1) return new Lease(QuoteRetentionEligibility.Unavailable);
        var provider = providers[0]; var request = new RecordAccessRequestContext(requestId, correlationId);
        string? workspaceId = null;
        foreach (var capability in new[] { "quotes.read", "quotes.delete" })
        {
            var grant = await access.AuthorizeResourceAsync("quotes", capability, provider.Descriptor.EnforceableFields.Keys.ToArray(),
                RecordAccessRepresentation.Full, request, ct);
            if (!grant.IsAllowed || grant.TrustedWorkspace is not { } trusted || grant.UnenforceableFieldKeys.Count != 0)
                return new Lease(QuoteRetentionEligibility.Unavailable);
            if (workspaceId is not null && workspaceId != trusted.WorkspaceId) return new Lease(QuoteRetentionEligibility.Unavailable);
            workspaceId = trusted.WorkspaceId;
            var record = await provider.ReadFactsAsync(trusted, quoteId, request, ct);
            if (!(await access.AuthorizeRecordAsync(grant, quoteId, record, "verifyQuoteArchiveRetention", request, ct)).IsAllowed)
                return new Lease(QuoteRetentionEligibility.Unavailable);
            if (capability == "quotes.delete" && (!grant.CanWrite("archivedAt") || !grant.CanWrite("archiveReason")))
                return new Lease(QuoteRetentionEligibility.Unavailable);
        }
        IDbContextTransaction? transaction = null;
        try
        {
            // Serializable indexed predicate protects even an absent reference until the archive commits.
            transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var blocked = await db.Deals.AsNoTracking().AnyAsync(d => d.WorkspaceId == workspaceId && d.WinEvidenceType == "QUOTE_ACCEPTED" && d.WinEvidenceSourceId == quoteId, ct);
            return new Lease(blocked ? QuoteRetentionEligibility.Blocked : QuoteRetentionEligibility.Clear, transaction);
        }
        catch (DbException)
        {
            if (transaction is not null) await transaction.DisposeAsync();
            logger.LogWarning("Quote archive verification unavailable for {Owner}; correlation {CorrelationId}", Owner, correlationId);
            return new Lease(QuoteRetentionEligibility.Unavailable);
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            throw;
        }
    }
    private sealed class Lease(QuoteRetentionEligibility eligibility, IDbContextTransaction? transaction = null) : IQuoteRetentionLease
    {
        public QuoteRetentionEligibility Eligibility => eligibility;
        public async ValueTask DisposeAsync() { if (transaction is not null) await transaction.DisposeAsync(); }
    }
}
