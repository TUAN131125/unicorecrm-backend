namespace UnicoreCRM.Sales.Quotes.Contracts;

internal enum ProofScanState { SCAN_PENDING, SCANNING, CLEAN, UNSAFE, SCAN_FAILED }
internal sealed record TrustedProofScanInput(string WorkspaceId,string CaseId,string ProofId,string Sha256,long ContentLength);
internal sealed record TrustedProofScanResult(string ScanId,string ProofId,string InputSha256,long InputLength,string ScannerIdentity,string EngineVersion,
    string SignatureVersion,DateTimeOffset? SignatureUpdatedAt,string RuntimeDigest,ProofScanState ScanStatus,DateTimeOffset StartedAt,DateTimeOffset CompletedAt,
    string Verdict,string ReasonCode,long DurationMs);
internal interface ITrustedProofScanner
{
    Task<TrustedProofScanResult> ScanAsync(TrustedProofScanInput input,Stream quarantinedContent,CancellationToken cancellationToken);
}
