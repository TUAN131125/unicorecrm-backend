using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;

internal static class QuoteProofModel
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<QuoteDocument>().HasAlternateKey(x=>new {x.WorkspaceId,x.QuoteId,x.DocumentId});
        model.Entity<QuoteAcceptanceCase>(e=>
        {
            e.ToTable("AcceptanceCases",t=>{
                t.HasCheckConstraint("CK_AcceptanceCase_State","[CaseStatus] COLLATE Latin1_General_100_BIN2 IN ('COLLECTING_PROOFS','QUARANTINED')");
                t.HasCheckConstraint("CK_AcceptanceCase_Version","[QuoteRevision]>=1 AND [ResourceVersion]>=1");
            });
            e.HasKey(x=>new{x.WorkspaceId,x.CaseId});
            foreach(var n in new[]{"WorkspaceId","CaseId","QuoteId","RootQuoteId","DocumentId","BuyerId","CreatedBy"}) e.Property<string>(n).HasMaxLength(128);
            foreach(var n in new[]{"CommercialFingerprint","IssuedPdfSha256"}) e.Property<string>(n).HasMaxLength(64);
            e.Property(x=>x.PolicyVersion).HasMaxLength(160); e.Property(x=>x.BuyerType).HasMaxLength(32);e.Property(x=>x.CaseStatus).HasMaxLength(32);
            e.Property(x=>x.ResourceVersion).IsConcurrencyToken();
            e.HasOne<QuoteDocument>().WithMany().HasForeignKey(x=>new{x.WorkspaceId,x.QuoteId,x.DocumentId}).HasPrincipalKey(x=>new{x.WorkspaceId,x.QuoteId,x.DocumentId}).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x=>new{x.WorkspaceId,x.QuoteId,x.DocumentId}).IsUnique();
            e.HasIndex(x=>new{x.WorkspaceId,x.QuoteId,x.CreatedAt,x.CaseId});
        });
        model.Entity<QuoteAcceptanceProof>(e=>
        {
            e.ToTable("AcceptanceProofs",t=>{
                t.HasCheckConstraint("CK_AcceptanceProof_Quarantine","[StorageStatus] COLLATE Latin1_General_100_BIN2 = 'QUARANTINED' AND [ScanStatus] COLLATE Latin1_General_100_BIN2 = 'SCAN_PENDING'");
                t.HasCheckConstraint("CK_AcceptanceProof_Bounds","[FileSize] BETWEEN 1 AND 10485760 AND [ResourceVersion]=1 AND [MediaType]='application/pdf'");
                t.HasCheckConstraint("CK_AcceptanceProof_Type","[EvidenceType] COLLATE Latin1_General_100_BIN2 IN ('CUSTOMER_CONSENT','IDENTITY','DELEGATION','SUPPORTING_DOCUMENT')");
            });
            e.HasKey(x=>new{x.WorkspaceId,x.ProofId});e.HasAlternateKey(x=>new{x.WorkspaceId,x.CaseId,x.ProofId});
            foreach(var n in new[]{"WorkspaceId","CaseId","ProofId","UploadedBy"}) e.Property<string>(n).HasMaxLength(128);
            e.Property(x=>x.FileName).HasMaxLength(120);e.Property(x=>x.Sha256).HasMaxLength(64);e.Property(x=>x.PolicyVersion).HasMaxLength(160);
            foreach(var n in new[]{"EvidenceType","MediaType","StorageStatus","ScanStatus","Retention"}) e.Property<string>(n).HasMaxLength(40);
            e.HasOne<QuoteAcceptanceCase>().WithMany().HasForeignKey(x=>new{x.WorkspaceId,x.CaseId}).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x=>new{x.WorkspaceId,x.CaseId,x.Sha256}).IsUnique();
            e.HasIndex(x=>new{x.WorkspaceId,x.CaseId,x.UploadedAt,x.ProofId});
        });
        model.Entity<QuoteAcceptanceProofContent>(e=>
        {
            e.ToTable("AcceptanceProofContents",t=>t.HasCheckConstraint("CK_AcceptanceProofContent_Bounds","[ContentLength] BETWEEN 1 AND 10485760 AND DATALENGTH([ProtectedBytes]) > [ContentLength]"));
            e.HasKey(x=>new{x.WorkspaceId,x.ProofId});
            foreach(var n in new[]{"WorkspaceId","CaseId","ProofId"}) e.Property<string>(n).HasMaxLength(128);
            e.Property(x=>x.Sha256).HasMaxLength(64);e.Property(x=>x.ProtectionFormat).HasMaxLength(40);e.Property(x=>x.ProtectedBytes).HasColumnType("varbinary(max)").IsRequired();
            e.HasOne<QuoteAcceptanceProof>().WithOne().HasForeignKey<QuoteAcceptanceProofContent>(x=>new{x.WorkspaceId,x.CaseId,x.ProofId}).HasPrincipalKey<QuoteAcceptanceProof>(x=>new{x.WorkspaceId,x.CaseId,x.ProofId}).OnDelete(DeleteBehavior.Restrict);
        });
    }
    internal static void Guard(QuotesDbContext db)
    {
        foreach(var entry in db.ChangeTracker.Entries<QuoteAcceptanceCase>())
        {
            if(entry.State==EntityState.Deleted || entry.State==EntityState.Modified && entry.Properties.Any(p=>p.IsModified && p.Metadata.Name is not ("CaseStatus" or "UpdatedAt" or "ResourceVersion")))
                throw new InvalidOperationException("Acceptance case binding is immutable.");
        }
    }
}
