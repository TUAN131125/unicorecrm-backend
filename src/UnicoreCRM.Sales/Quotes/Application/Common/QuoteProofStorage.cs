using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed class QuoteProofStorageOptions { public Dictionary<string, QuoteProofStoragePolicy> Workspaces { get; set; } = new(StringComparer.Ordinal); }
internal sealed class QuoteProofStoragePolicy
{
    public bool Enabled { get; set; }
    public string Version { get; set; } = "";
    public int FileBytes { get; set; } = 10485760;
    public int CaseFiles { get; set; } = 5;
    public long CaseBytes { get; set; } = 26214400;
    public long WorkspaceBytes { get; set; } = 268435456;
    public int WorkspaceFiles { get; set; } = 250;
    public int WorkspaceCases { get; set; } = 100;
}
internal interface IQuoteProofPolicyReader { QuoteProofStoragePolicy? Read(string workspaceId); }
internal sealed class QuoteProofPolicyReader(IOptionsMonitor<QuoteProofStorageOptions> options) : IQuoteProofPolicyReader
{
    public QuoteProofStoragePolicy? Read(string workspaceId)
    {
        QuoteProofStorageOptions value;
        try { value=options.CurrentValue; }
        catch(Exception e) when(e is InvalidOperationException or OptionsValidationException) { return null; }
        var row=value?.Workspaces?.FirstOrDefault(x=>x.Key==workspaceId).Value;
        if (row is null || !row.Enabled || string.IsNullOrWhiteSpace(row.Version) || row.Version.Length>100 || row.Version.Any(char.IsControl)
            || row.FileBytes is < 1 or > 10485760 || row.CaseFiles is < 1 or > 100 || row.CaseBytes<row.FileBytes
            || row.WorkspaceBytes<row.CaseBytes || row.WorkspaceFiles<row.CaseFiles || row.WorkspaceCases is < 1 or > 10000) return null;
        return new() { Enabled=true, Version=row.Version+":"+QuoteApprovalAssessment.Hash(FormattableString.Invariant($"{row.FileBytes}|{row.CaseFiles}|{row.CaseBytes}|{row.WorkspaceBytes}|{row.WorkspaceFiles}|{row.WorkspaceCases}"))[..16],
            FileBytes=row.FileBytes, CaseFiles=row.CaseFiles, CaseBytes=row.CaseBytes, WorkspaceBytes=row.WorkspaceBytes, WorkspaceFiles=row.WorkspaceFiles, WorkspaceCases=row.WorkspaceCases };
    }
}
internal sealed record QuoteProofUsage(int CaseCount, int WorkspaceFiles, long WorkspaceBytes, int CaseFiles, long CaseBytes);
internal interface IQuoteProofPersistence
{
    Task<QuoteAcceptanceCase?> CaseAsync(string workspace, string quote, string caseId, bool tracked, CancellationToken ct);
    Task<QuoteAcceptanceCase?> DocumentCaseAsync(string workspace, string quote, string document, CancellationToken ct);
    Task<IReadOnlyList<QuoteAcceptanceCase>> CasesAsync(string workspace,string quote,int offset,int limit,CancellationToken ct);
    Task<IReadOnlyList<QuoteAcceptanceProof>> ProofsAsync(string workspace,string caseId,int offset,int limit,CancellationToken ct);
    Task<QuoteAcceptanceProof?> ProofAsync(string workspace,string caseId,string proofId,CancellationToken ct);
    Task<bool> HasHashAsync(string workspace,string caseId,string hash,CancellationToken ct);
    Task<QuoteProofUsage> UsageAsync(string workspace,string? caseId,CancellationToken ct);
    void AddCase(QuoteAcceptanceCase row);
    void AddProof(QuoteAcceptanceProof row,byte[] encryptedBytes);
}
internal interface IQuoteProofCipher { byte[] Protect(string workspace,string caseId,string proofId,byte[] bytes); }
internal interface IQuoteProofCommitProbe { void AfterStorageFlush(string workspaceId); }
internal static class QuoteProofInput
{
    internal const int MaximumBytes=10485760;
    internal static readonly string[] EvidenceTypes=["CUSTOMER_CONSENT","IDENTITY","DELEGATION","SUPPORTING_DOCUMENT"];
    internal static string? FileName(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded) || encoded.Length>1024) return null;
        string decoded; try { decoded=Uri.UnescapeDataString(encoded); } catch (UriFormatException) { return null; }
        decoded=decoded.Replace('\\','/').Split('/').Last();
        var clean=new string(decoded.Where(c=>!char.IsControl(c) && CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.Format && c is not '<' and not '>' and not ':' and not '"' and not '|' and not '?' and not '*').ToArray()).Trim(' ','.');
        if (clean.Length==0) return null;
        if (clean.EndsWith(".pdf",StringComparison.OrdinalIgnoreCase)) clean=clean[..^4];
        if (clean.Length>116) clean=clean[..116];
        return clean.Trim(' ','.')+".pdf";
    }
    // Only candidacy; structural validity, active content and malware require B6.2.
    internal static bool PdfCandidate(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length<16 || !bytes[..5].SequenceEqual("%PDF-"u8)) return false;
        if (!(bytes[5]=='1' && bytes[6]=='.' && bytes[7]>='0' && bytes[7]<='7') && !bytes[5..8].SequenceEqual("2.0"u8)) return false;
        return bytes[Math.Max(0,bytes.Length-1024)..].IndexOf("%%EOF"u8)>=0;
    }
}
