using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Application.Common;
internal sealed record QuoteProofRuntimeSnapshot(string Digest,string EngineVersion,string SignatureVersion,DateTimeOffset SignatureUpdatedAt,string QpdfSha256);
internal interface IQuoteProofRuntimeAuthority { Task<QuoteProofRuntimeSnapshot?> ReadSnapshotAsync(CancellationToken ct); }
internal interface IQuoteProofStructuralValidator { Task<ProofParserResult> ValidateAsync(byte[] bytes,CancellationToken ct); }
internal sealed record ProofParserResult(string Status,string ReasonCode,string InputSha256,int InputLength,string ParserIdentity,string ParserVersion,string ParserSha256,long DurationMs,ProofPdfInspection? Inspection=null);
internal interface IQuoteProofScanStore
{
    Task<IReadOnlyList<string>> CandidatesAsync(string workspace,string epoch,DateTimeOffset now,int limit,CancellationToken ct);
    Task<QuoteAcceptanceProof?> ProofAsync(string workspace,string proof,CancellationToken ct);
    Task<string?> CaseQuoteIdAsync(string workspace,string caseId,CancellationToken ct);
    Task<QuoteProofScanJob?> JobAsync(string workspace,string proof,bool tracked,CancellationToken ct);
    Task<QuoteProofScanAttempt?> AttemptAsync(string workspace,string attempt,CancellationToken ct);
    Task<IReadOnlyList<QuoteProofScanAttempt>> AttemptsAsync(string workspace,IReadOnlyList<string> attempts,CancellationToken ct);
    Task<QuoteProofScanResult?> ResultAsync(string workspace,string attempt,CancellationToken ct);
    Task<IReadOnlyList<QuoteProofScanAttempt>> HistoryAsync(string workspace,string proof,int offset,int limit,CancellationToken ct);
    Task<IReadOnlyList<QuoteProofScanResult>> ResultsAsync(string workspace,IReadOnlyList<string> attempts,CancellationToken ct);
    Task<IReadOnlyList<QuoteProofScanJob>> JobsAsync(string workspace,IReadOnlyList<string> proofs,CancellationToken ct);
    Task<byte[]?> ReadBytesAsync(string workspace,string caseId,string proof,CancellationToken ct);
    void AddJob(QuoteProofScanJob job);void AddAttempt(QuoteProofScanAttempt attempt);void AddResult(QuoteProofScanResult result);
}
internal interface IQuoteProofScanCommitProbe { void AfterResultFlush(string workspace);Task AfterClaimAsync(string workspace,CancellationToken ct); }
