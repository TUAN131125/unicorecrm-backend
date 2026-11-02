using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Sales.Quotes.Domain;
using UnicoreCRM.Sales.Quotes.Application.Common;
namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;

internal sealed class EfQuoteIdentityPersistence(QuotesDbContext db):IQuoteIdentityPersistence
{
    public Task<QuoteIdentityVersion?> CurrentAsync(string w,string c,CancellationToken ct)=>db.Set<QuoteIdentityVersion>().AsNoTracking().Where(x=>x.WorkspaceId==w && x.CaseId==c).OrderByDescending(x=>x.ResourceVersion).FirstOrDefaultAsync(ct);
    public async Task<IReadOnlyList<QuoteIdentityVersion>> HistoryAsync(string w,string c,int offset,int limit,CancellationToken ct)=>await db.Set<QuoteIdentityVersion>().AsNoTracking().Where(x=>x.WorkspaceId==w && x.CaseId==c).OrderByDescending(x=>x.ResourceVersion).Skip(offset).Take(limit).ToArrayAsync(ct);
    public Task<QuoteEvidenceSourceVersion?> SourceAsync(string w,string s,CancellationToken ct)=>db.Set<QuoteEvidenceSourceVersion>().AsNoTracking().Where(x=>x.WorkspaceId==w && x.SourceId==s).OrderByDescending(x=>x.ResourceVersion).FirstOrDefaultAsync(ct);
    public async Task<IReadOnlyList<QuoteEvidenceSourceVersion>> SourceHistoryAsync(string w,string s,int offset,int limit,CancellationToken ct)=>await db.Set<QuoteEvidenceSourceVersion>().AsNoTracking().Where(x=>x.WorkspaceId==w && x.SourceId==s).OrderByDescending(x=>x.ResourceVersion).Skip(offset).Take(limit).ToArrayAsync(ct);
    public async Task<IReadOnlyList<QuoteEvidenceSourceVersion>> SourcesAsync(string w,string type,string buyer,int offset,int limit,CancellationToken ct)
    {
        var rows=db.Set<QuoteEvidenceSourceVersion>().AsNoTracking().Where(x=>x.WorkspaceId==w && x.BuyerType==type && x.BuyerId==buyer);
        var versions=rows.GroupBy(x=>x.SourceId).Select(g=>new{Id=g.Key,Version=g.Max(x=>x.ResourceVersion)});
        return await (from row in rows join v in versions on new{Id=row.SourceId,Version=row.ResourceVersion} equals new{v.Id,v.Version} orderby row.SourceId select row).Skip(offset).Take(limit).ToArrayAsync(ct);
    }
    public async Task<IReadOnlyList<QuoteAcceptanceProof>> ProofsAsync(string w,string c,IReadOnlyList<string> ids,CancellationToken ct)=>await db.Set<QuoteAcceptanceProof>().AsNoTracking().Where(x=>x.WorkspaceId==w && x.CaseId==c && ids.Contains(x.ProofId)).ToArrayAsync(ct);
    public Task<QuoteIdentityVersion?> ConfirmationAsync(string w,string c,string proofId,string sha256,CancellationToken ct)=>db.Set<QuoteIdentityVersion>().AsNoTracking()
        .Where(x=>x.WorkspaceId==w && x.CaseId==c && x.Kind=="SOURCE_CHECK_ATTEMPTED" && x.SnapshotJson.Contains("\"manualCheck\"") && (x.SnapshotJson.Contains("\"proofId\":\""+proofId+"\"")||x.SnapshotJson.Contains("\"sha256\":\""+sha256+"\"")))
        .OrderBy(x=>x.ResourceVersion).FirstOrDefaultAsync(ct);
    public async Task<IReadOnlyList<QuoteIdentityVersion>> ReviewsAsync(string w,string c,int offset,int limit,CancellationToken ct)=>await db.Set<QuoteIdentityVersion>().AsNoTracking().Where(x=>x.WorkspaceId==w && x.CaseId==c && x.Kind=="REVIEW_RECORDED").OrderByDescending(x=>x.ResourceVersion).Skip(offset).Take(limit).ToArrayAsync(ct);
    public Task<QuoteIdentityVersion?> LatestKindAsync(string w,string c,string kind,CancellationToken ct)=>db.Set<QuoteIdentityVersion>().AsNoTracking().Where(x=>x.WorkspaceId==w && x.CaseId==c && x.Kind==kind).OrderByDescending(x=>x.ResourceVersion).FirstOrDefaultAsync(ct);
    public void AddVersion(QuoteIdentityVersion row)=>db.Add(row);public void AddSource(QuoteEvidenceSourceVersion row)=>db.Add(row);
}
