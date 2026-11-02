using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;

internal sealed class EfQuoteProofScanStore(QuotesDbContext db,IQuoteProofCipher cipher):IQuoteProofScanStore
{
    public async Task<IReadOnlyList<string>> CandidatesAsync(string w,string epoch,DateTimeOffset now,int limit,CancellationToken ct)=>
        await (from p in db.Set<QuoteAcceptanceProof>().AsNoTracking()
               join j in db.Set<QuoteProofScanJob>().AsNoTracking() on new{p.WorkspaceId,p.ProofId} equals new{j.WorkspaceId,j.ProofId} into jobs
               from j in jobs.DefaultIfEmpty()
               where p.WorkspaceId==w && (j==null || j.Status=="SCANNING" && j.LeaseExpiresAt<=now || j.Status!="NEEDS_INTERVENTION" && j.Status!="SCANNING" && j.Status!="UNSAFE" && j.Status!="INVALID_PDF" && j.NextAttemptAt<=now && (j.Status!="CLEAN" || j.Epoch!=epoch))
               orderby p.UploadedAt,p.ProofId select p.ProofId).Take(Math.Clamp(limit,1,20)).ToArrayAsync(ct);
    public Task<QuoteAcceptanceProof?> ProofAsync(string w,string p,CancellationToken ct)=>db.Set<QuoteAcceptanceProof>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkspaceId==w && x.ProofId==p,ct);
    public Task<string?> CaseQuoteIdAsync(string w,string c,CancellationToken ct)=>db.Set<QuoteAcceptanceCase>().AsNoTracking().Where(x=>x.WorkspaceId==w && x.CaseId==c).Select(x=>x.QuoteId).SingleOrDefaultAsync(ct);
    public Task<QuoteProofScanJob?> JobAsync(string w,string p,bool tracked,CancellationToken ct)=>(tracked?db.Set<QuoteProofScanJob>():db.Set<QuoteProofScanJob>().AsNoTracking()).SingleOrDefaultAsync(x=>x.WorkspaceId==w && x.ProofId==p,ct);
    public Task<QuoteProofScanAttempt?> AttemptAsync(string w,string a,CancellationToken ct)=>db.Set<QuoteProofScanAttempt>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkspaceId==w && x.AttemptId==a,ct);
    public async Task<IReadOnlyList<QuoteProofScanAttempt>> AttemptsAsync(string w,IReadOnlyList<string> a,CancellationToken ct)=>await db.Set<QuoteProofScanAttempt>().AsNoTracking().Where(x=>x.WorkspaceId==w && a.Contains(x.AttemptId)).ToArrayAsync(ct);
    public Task<QuoteProofScanResult?> ResultAsync(string w,string a,CancellationToken ct)=>db.Set<QuoteProofScanResult>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkspaceId==w && x.AttemptId==a,ct);
    public async Task<IReadOnlyList<QuoteProofScanAttempt>> HistoryAsync(string w,string p,int offset,int limit,CancellationToken ct)=>await db.Set<QuoteProofScanAttempt>().AsNoTracking().Where(x=>x.WorkspaceId==w && x.ProofId==p).OrderByDescending(x=>x.AttemptNumber).Skip(offset).Take(limit).ToArrayAsync(ct);
    public async Task<IReadOnlyList<QuoteProofScanResult>> ResultsAsync(string w,IReadOnlyList<string> a,CancellationToken ct)=>await db.Set<QuoteProofScanResult>().AsNoTracking().Where(x=>x.WorkspaceId==w && a.Contains(x.AttemptId)).ToArrayAsync(ct);
    public async Task<IReadOnlyList<QuoteProofScanJob>> JobsAsync(string w,IReadOnlyList<string> p,CancellationToken ct)=>await db.Set<QuoteProofScanJob>().AsNoTracking().Where(x=>x.WorkspaceId==w && p.Contains(x.ProofId)).ToArrayAsync(ct);
    public async Task<byte[]?> ReadBytesAsync(string w,string c,string p,CancellationToken ct)
    {
        var row=await db.Set<QuoteAcceptanceProofContent>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkspaceId==w && x.CaseId==c && x.ProofId==p,ct);
        return row is null?null:((QuoteProofCipher)cipher).Unprotect(row.WorkspaceId,row.CaseId,row.ProofId,row.ProtectedBytes);
    }
    public void AddJob(QuoteProofScanJob j)=>db.Add(j);
    public void AddAttempt(QuoteProofScanAttempt a)=>db.Add(a);
    public void AddResult(QuoteProofScanResult r)=>db.Add(r);
}
