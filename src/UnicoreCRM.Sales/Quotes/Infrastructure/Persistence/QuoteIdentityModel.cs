using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;

internal static class QuoteIdentityModel
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<QuoteAcceptanceCase>().HasAlternateKey(x=>new{x.WorkspaceId,x.CaseId,x.QuoteId});
        model.Entity<QuoteIdentityVersion>(e=>
        {
            e.ToTable("IdentityAuthorityVersions",t=>
            {
                t.HasCheckConstraint("CK_IdentityAuthorityVersion_Json","ISJSON([SnapshotJson])=1 AND DATALENGTH([SnapshotJson])<=131072");
                t.HasCheckConstraint("CK_IdentityAuthorityVersion_Kind","[Kind] COLLATE Latin1_General_100_BIN2 IN ('BINDING_RECORDED','SOURCE_CHECK_ATTEMPTED','ASSESSMENT_RECORDED','REVIEW_RECORDED')");
                t.HasCheckConstraint("CK_IdentityAuthorityVersion_Review","[Kind]<>'REVIEW_RECORDED' OR (COALESCE(JSON_VALUE([SnapshotJson],'$.independentReview.assuranceLevel'),'')='DEVELOPMENT_SIMULATION' AND COALESCE(JSON_VALUE([SnapshotJson],'$.independentReview.actualSourceVerification'),'')='false' AND COALESCE(JSON_VALUE([SnapshotJson],'$.independentReview.environment'),'')='Development' AND COALESCE(JSON_VALUE([SnapshotJson],'$.independentReview.decision'),'') IN ('APPROVE_SIMULATION','REQUEST_MORE_EVIDENCE','REJECT_REVIEW') AND COALESCE(JSON_VALUE([SnapshotJson],'$.independentReview.reviewerId'),'')=[ActorId] AND COALESCE(JSON_VALUE([SnapshotJson],'$.independentReview.caseId'),'')=[CaseId] AND COALESCE(JSON_VALUE([SnapshotJson],'$.independentReview.quoteId'),'')=[QuoteId] AND JSON_VALUE([SnapshotJson],'$.independentReview.reviewDecisionId') IS NOT NULL)");
                t.HasCheckConstraint("CK_IdentityAuthorityVersion_Number","[ResourceVersion]>=1");
            });
            e.HasKey(x=>new{x.WorkspaceId,x.CaseId,x.ResourceVersion});
            foreach(var n in new[]{"WorkspaceId","CaseId","QuoteId","ActorId"})e.Property<string>(n).HasMaxLength(128);
            e.Property(x=>x.Kind).HasMaxLength(40);e.Property(x=>x.PolicyVersion).HasMaxLength(180);e.Property(x=>x.Reason).HasMaxLength(500);
            e.Property(x=>x.SnapshotJson).HasColumnType("nvarchar(max)").IsRequired();
            e.HasOne<QuoteAcceptanceCase>().WithMany().HasForeignKey(x=>new{x.WorkspaceId,x.CaseId,x.QuoteId}).HasPrincipalKey(x=>new{x.WorkspaceId,x.CaseId,x.QuoteId}).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<QuoteEvidenceSourceVersion>(e=>
        {
            e.ToTable("AcceptanceEvidenceSourceVersions",t=>
            {
                t.HasCheckConstraint("CK_AcceptanceSourceVersion_Json","ISJSON([SnapshotJson])=1 AND DATALENGTH([SnapshotJson])<=32768");
                t.HasCheckConstraint("CK_AcceptanceSourceVersion_State","[Status] COLLATE Latin1_General_100_BIN2 IN ('DRAFT','APPROVED','REVOKED')");
                t.HasCheckConstraint("CK_AcceptanceSourceVersion_Approval","[Status]<>'APPROVED' OR (COALESCE(JSON_VALUE([SnapshotJson],'$.assuranceLevel'),'')='DEVELOPMENT_SIMULATION' AND COALESCE(JSON_VALUE([SnapshotJson],'$.verificationMethod'),'')='ORGANIZATION_ISSUED_AUTHORITY_WITH_INDEPENDENT_SOURCE_CHECK' AND JSON_VALUE([SnapshotJson],'$.approvedBy') IS NOT NULL AND JSON_VALUE([SnapshotJson],'$.createdBy') IS NOT NULL AND JSON_VALUE([SnapshotJson],'$.approvedBy')<>JSON_VALUE([SnapshotJson],'$.createdBy') AND JSON_QUERY([SnapshotJson],'$.evidence') IS NOT NULL)");
                t.HasCheckConstraint("CK_AcceptanceSourceVersion_Buyer","[BuyerType] COLLATE Latin1_General_100_BIN2 IN ('CONTACT','ORGANIZATION_ACCOUNT')");
                t.HasCheckConstraint("CK_AcceptanceSourceVersion_Number","[ResourceVersion]>=1");
            });
            e.HasKey(x=>new{x.WorkspaceId,x.SourceId,x.ResourceVersion});
            foreach(var n in new[]{"WorkspaceId","SourceId","BuyerId","ActorId"})e.Property<string>(n).HasMaxLength(128);
            e.Property(x=>x.BuyerType).HasMaxLength(32);e.Property(x=>x.Status).HasMaxLength(20);e.Property(x=>x.SnapshotJson).HasColumnType("nvarchar(max)").IsRequired();
            e.HasIndex(x=>new{x.WorkspaceId,x.BuyerType,x.BuyerId,x.SourceId,x.ResourceVersion});
        });
    }
}
