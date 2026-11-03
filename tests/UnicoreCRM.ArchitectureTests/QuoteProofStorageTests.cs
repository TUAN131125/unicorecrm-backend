using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Domain;
using UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;
using Xunit;
namespace UnicoreCRM.ArchitectureTests;

public sealed class QuoteProofStorageTests
{
    [Theory]
    [InlineData("%PDF-1.7\nSAFE SYNTHETIC\n%%EOF",true)]
    [InlineData("%PDF-2.0\nSAFE SYNTHETIC\n%%EOF",true)]
    [InlineData("%PDF-9.9\nSAFE SYNTHETIC\n%%EOF",false)]
    [InlineData("%PDF-1.7\ntruncated",false)]
    [InlineData("not a PDF",false)]
    public void Signature_check_is_bounded_candidacy_only(string content,bool candidate)=>Assert.Equal(candidate,QuoteProofInput.PdfCandidate(Encoding.ASCII.GetBytes(content)));
    [Fact]
    public void File_names_are_display_only_without_paths_controls_or_bidi()
    {
        Assert.Equal("consent.pdf",QuoteProofInput.FileName(Uri.EscapeDataString("C:\\secret\\consent.pdf")));
        Assert.Equal("secret.pdf",QuoteProofInput.FileName(Uri.EscapeDataString("../../secret.pdf")));
        Assert.Equal("safe.pdf",QuoteProofInput.FileName(Uri.EscapeDataString("s\u202Eafe\n.pdf")));
        Assert.Null(QuoteProofInput.FileName(new string('a',1025)));
    }
    [Fact]
    public void Policy_requires_exact_workspace_and_bounds_and_versions_effective_limits()
    {
        var value=new QuoteProofStorageOptions();value.Workspaces["workspace"]=new(){Enabled=true,Version="b61"};var reader=new QuoteProofPolicyReader(new Monitor(value));
        Assert.Null(reader.Read("Workspace"));var before=reader.Read("workspace");Assert.NotNull(before);
        value.Workspaces["workspace"].CaseFiles=4;Assert.NotEqual(before.Version,reader.Read("workspace")!.Version);
        value.Workspaces["workspace"].FileBytes=10485761;Assert.Null(reader.Read("workspace"));
        Assert.Null(new QuoteProofPolicyReader(new InvalidMonitor()).Read("workspace"));
    }
    [Fact]
    public void Protected_bytes_require_exact_workspace_case_proof_purpose_and_reject_tamper()
    {
        var cipher=new QuoteProofCipher(new EphemeralDataProtectionProvider());var plain=Encoding.ASCII.GetBytes("safe synthetic proof");
        var encrypted=cipher.Protect("w","c","p",plain);Assert.NotEqual(plain,encrypted);Assert.Equal(plain,cipher.Unprotect("w","c","p",encrypted));
        Assert.Throws<CryptographicException>(()=>cipher.Unprotect("other","c","p",encrypted));
        Assert.Throws<CryptographicException>(()=>cipher.Unprotect("w","other","p",encrypted));
        Assert.Throws<CryptographicException>(()=>cipher.Unprotect("w","c","other",encrypted));
        encrypted[^1]^=1;Assert.Throws<CryptographicException>(()=>cipher.Unprotect("w","c","p",encrypted));
    }
    [Fact]
    public async Task Proof_and_content_cannot_be_updated_or_deleted()
    {
        using var db=Context();var proof=Proof();db.Attach(proof);db.Entry(proof).Property(x=>x.Sha256).CurrentValue=new string('B',64);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync(false));
        Assert.Throws<InvalidOperationException>(()=>db.SaveChanges());
        db.ChangeTracker.Clear();db.Attach(proof);db.Remove(proof);await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
        db.ChangeTracker.Clear();var content=new QuoteAcceptanceProofContent("w","c","p",new byte[200],new string('A',64),16);db.Attach(content);db.Remove(content);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
    }
    [Fact]
    public async Task Case_binding_is_immutable_and_upload_only_advances_case_version()
    {
        using var db=Context();var row=new QuoteAcceptanceCase("w","c","q","root",1,"doc",new string('A',64),new string('B',64),"CONTACT","buyer","b61","actor",DateTimeOffset.UtcNow);
        row.RecordQuarantine(DateTimeOffset.UtcNow);Assert.Equal(2,row.ResourceVersion);Assert.Equal("QUARANTINED",row.CaseStatus);
        db.Attach(row);db.Entry(row).Property(x=>x.DocumentId).CurrentValue="forged";
        await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
        var proof=Proof();Assert.Equal("QUARANTINED",proof.StorageStatus);Assert.Equal("SCAN_PENDING",proof.ScanStatus);
        Assert.DoesNotContain(typeof(QuoteAcceptanceProof).GetMethods(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic),m=>m.Name.Contains("Clean") || m.Name.Contains("Verify"));
    }
    [Fact]
    public void Relational_model_enforces_workspace_case_proof_and_document_keys()
    {
        using var db=Context();var c=db.Model.FindEntityType(typeof(QuoteAcceptanceCase))!;
        Assert.Contains(c.GetForeignKeys(),k=>k.Properties.Select(x=>x.Name).SequenceEqual(new[]{"WorkspaceId","QuoteId","DocumentId"}));
        var p=db.Model.FindEntityType(typeof(QuoteAcceptanceProofContent))!;
        Assert.Contains(p.GetForeignKeys(),k=>k.Properties.Select(x=>x.Name).SequenceEqual(new[]{"WorkspaceId","CaseId","ProofId"}));
        Assert.True(c.FindProperty("ResourceVersion")!.IsConcurrencyToken);
    }
    private static QuoteAcceptanceProof Proof()=>new("w","c","p","SUPPORTING_DOCUMENT","safe.pdf",16,new string('A',64),"b61","actor",DateTimeOffset.UtcNow);
    private static QuotesDbContext Context()=>new(new DbContextOptionsBuilder<QuotesDbContext>().UseSqlServer("Server=unused;Database=not-opened;Trusted_Connection=True;TrustServerCertificate=True").Options);
    private sealed class Monitor(QuoteProofStorageOptions value):IOptionsMonitor<QuoteProofStorageOptions>
    {public QuoteProofStorageOptions CurrentValue=>value;public QuoteProofStorageOptions Get(string? name)=>value;public IDisposable? OnChange(Action<QuoteProofStorageOptions,string?> listener)=>null;}
    private sealed class InvalidMonitor:IOptionsMonitor<QuoteProofStorageOptions>
    {public QuoteProofStorageOptions CurrentValue=>throw new InvalidOperationException("Invalid binding");public QuoteProofStorageOptions Get(string? name)=>CurrentValue;public IDisposable? OnChange(Action<QuoteProofStorageOptions,string?> listener)=>null;}
}
