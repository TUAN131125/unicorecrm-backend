using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;

internal sealed class EfQuoteProofPersistence(QuotesDbContext db) : IQuoteProofPersistence
{
    public Task<QuoteAcceptanceCase?> CaseAsync(string w,string q,string c,bool tracked,CancellationToken ct) =>
        (tracked?db.Set<QuoteAcceptanceCase>():db.Set<QuoteAcceptanceCase>().AsNoTracking()).SingleOrDefaultAsync(x=>x.WorkspaceId==w && x.QuoteId==q && x.CaseId==c,ct);
    public Task<QuoteAcceptanceCase?> DocumentCaseAsync(string w,string q,string d,CancellationToken ct) =>
        db.Set<QuoteAcceptanceCase>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkspaceId==w && x.QuoteId==q && x.DocumentId==d,ct);
    public async Task<IReadOnlyList<QuoteAcceptanceCase>> CasesAsync(string w,string q,int offset,int limit,CancellationToken ct) =>
        await db.Set<QuoteAcceptanceCase>().AsNoTracking().Where(x=>x.WorkspaceId==w && x.QuoteId==q).OrderBy(x=>x.CreatedAt).ThenBy(x=>x.CaseId).Skip(offset).Take(limit).ToArrayAsync(ct);
    public async Task<IReadOnlyList<QuoteAcceptanceProof>> ProofsAsync(string w,string c,int offset,int limit,CancellationToken ct) =>
        await db.Set<QuoteAcceptanceProof>().AsNoTracking().Where(x=>x.WorkspaceId==w && x.CaseId==c).OrderBy(x=>x.UploadedAt).ThenBy(x=>x.ProofId).Skip(offset).Take(limit).ToArrayAsync(ct);
    public Task<QuoteAcceptanceProof?> ProofAsync(string w,string c,string p,CancellationToken ct) =>
        db.Set<QuoteAcceptanceProof>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkspaceId==w && x.CaseId==c && x.ProofId==p,ct);
    public Task<bool> HasHashAsync(string w,string c,string hash,CancellationToken ct) => db.Set<QuoteAcceptanceProof>().AnyAsync(x=>x.WorkspaceId==w && x.CaseId==c && x.Sha256==hash,ct);
    public async Task<QuoteProofUsage> UsageAsync(string w,string? c,CancellationToken ct)
    {
        var rows=db.Set<QuoteAcceptanceProof>().Where(x=>x.WorkspaceId==w);
        return new(await db.Set<QuoteAcceptanceCase>().CountAsync(x=>x.WorkspaceId==w,ct),await rows.CountAsync(ct),await rows.SumAsync(x=>(long?)x.FileSize,ct)??0,
            await rows.CountAsync(x=>x.CaseId==c,ct),await rows.Where(x=>x.CaseId==c).SumAsync(x=>(long?)x.FileSize,ct)??0);
    }
    public void AddCase(QuoteAcceptanceCase row)=>db.Add(row);
    public void AddProof(QuoteAcceptanceProof row,byte[] encrypted)
    { db.Add(row); db.Add(new QuoteAcceptanceProofContent(row.WorkspaceId,row.CaseId,row.ProofId,encrypted,row.Sha256,row.FileSize)); }
}
