using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;

internal static class QuoteProofScanModel
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<QuoteProofScanJob>(e=>
        {
            e.ToTable("AcceptanceProofScanJobs",t=>
            {
                t.HasCheckConstraint("CK_ProofScanJob_Bounds","[LifetimeAttempts] BETWEEN 0 AND 12 AND [EpochAttempts] BETWEEN 0 AND 3 AND [EpochAttempts]<=[LifetimeAttempts] AND [ResourceVersion]>=1 AND [InputLength] BETWEEN 1 AND 10485760");
                t.HasCheckConstraint("CK_ProofScanJob_State","[Status] COLLATE Latin1_General_100_BIN2 IN ('SCAN_PENDING','SCANNING','CLEAN','UNSAFE','INVALID_PDF','SCAN_FAILED','NEEDS_INTERVENTION')");
                t.HasCheckConstraint("CK_ProofScanJob_Lease","([Status]='SCANNING' AND [AttemptId] IS NOT NULL AND [LeaseId] IS NOT NULL AND [LeaseExpiresAt] IS NOT NULL) OR ([Status]<>'SCANNING' AND [LeaseId] IS NULL AND [LeaseExpiresAt] IS NULL)");
            });
            e.HasKey(x=>new{x.WorkspaceId,x.ProofId});
            foreach(var n in new[]{"WorkspaceId","CaseId","ProofId","AttemptId","LeaseId","LastResultAttemptId"})e.Property<string>(n).HasMaxLength(128);
            foreach(var n in new[]{"InputSha256","Epoch"})e.Property<string>(n).HasMaxLength(64);
            e.Property(x=>x.Status).HasMaxLength(32);e.Property(x=>x.InterventionReason).HasMaxLength(100);
            e.Property(x=>x.ResourceVersion).IsConcurrencyToken();
            e.HasOne<QuoteAcceptanceProof>().WithMany().HasForeignKey(x=>new{x.WorkspaceId,x.CaseId,x.ProofId}).HasPrincipalKey(x=>new{x.WorkspaceId,x.CaseId,x.ProofId}).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x=>new{x.WorkspaceId,x.Status,x.NextAttemptAt});
        });
        model.Entity<QuoteProofScanAttempt>(e=>
        {
            e.ToTable("AcceptanceProofScanAttempts",t=>t.HasCheckConstraint("CK_ProofScanAttempt_Bounds","[AttemptNumber] BETWEEN 1 AND 12 AND [InputLength] BETWEEN 1 AND 10485760 AND [LeaseExpiresAt]>[StartedAt]"));
            e.HasKey(x=>new{x.WorkspaceId,x.AttemptId});
            e.HasAlternateKey(x=>new{x.WorkspaceId,x.CaseId,x.ProofId,x.AttemptId});
            foreach(var n in new[]{"WorkspaceId","CaseId","ProofId","AttemptId","LeaseId"})e.Property<string>(n).HasMaxLength(128);
            foreach(var n in new[]{"InputSha256","Epoch","PolicyDigest","RuntimeDigest"})e.Property<string>(n).HasMaxLength(64);
            e.Property(x=>x.PolicyVersion).HasMaxLength(160);
            e.HasOne<QuoteAcceptanceProof>().WithMany().HasForeignKey(x=>new{x.WorkspaceId,x.CaseId,x.ProofId}).HasPrincipalKey(x=>new{x.WorkspaceId,x.CaseId,x.ProofId}).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x=>new{x.WorkspaceId,x.ProofId,x.AttemptNumber}).IsUnique();
        });
        model.Entity<QuoteProofScanResult>(e=>
        {
            e.ToTable("AcceptanceProofScanResults",t=>
            {
                t.HasCheckConstraint("CK_ProofScanResult_Json","ISJSON([ProvenanceJson])=1");
                t.HasCheckConstraint("CK_ProofScanResult_Outcome","[Outcome] COLLATE Latin1_General_100_BIN2 IN ('CLEAN','UNSAFE','INVALID_PDF','SCAN_FAILED')");
                t.HasCheckConstraint("CK_ProofScanResult_Clean","[Outcome]<>'CLEAN' OR ([ParserStatus]='PASS' AND [MalwareVerdict]='CLEAN')");
            });
            e.HasKey(x=>new{x.WorkspaceId,x.AttemptId});
            foreach(var n in new[]{"WorkspaceId","CaseId","ProofId","AttemptId"})e.Property<string>(n).HasMaxLength(128);
            foreach(var n in new[]{"Outcome","ParserStatus","MalwareVerdict"})e.Property<string>(n).HasMaxLength(40);
            e.Property(x=>x.ReasonCode).HasMaxLength(160);e.Property(x=>x.ProvenanceJson).HasMaxLength(16000);
            e.HasOne<QuoteProofScanAttempt>().WithOne().HasForeignKey<QuoteProofScanResult>(x=>new{x.WorkspaceId,x.CaseId,x.ProofId,x.AttemptId}).HasPrincipalKey<QuoteProofScanAttempt>(x=>new{x.WorkspaceId,x.CaseId,x.ProofId,x.AttemptId}).OnDelete(DeleteBehavior.Restrict);
        });
    }
    internal static void Guard(QuotesDbContext db)
    {
        foreach(var e in db.ChangeTracker.Entries<QuoteProofScanJob>())
            if(e.State==EntityState.Deleted || e.State==EntityState.Modified && e.Properties.Any(p=>p.IsModified && p.Metadata.Name is "WorkspaceId" or "CaseId" or "ProofId" or "InputSha256" or "InputLength"))
                throw new InvalidOperationException("Scan job binding is immutable.");
    }
}
