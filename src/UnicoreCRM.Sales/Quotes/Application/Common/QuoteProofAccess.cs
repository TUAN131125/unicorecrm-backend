using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed record QuoteProofAccessGrant(Quote Quote,IReadOnlyList<QuoteAccess> Grants);
internal sealed class QuoteProofAccess(QuoteAuthorization authorization,IQuotesPersistence quotes,IQuoteDocumentPersistence documents,
    QuoteDocumentCommands documentAccess,QuoteVerifiedAcceptanceAssessment assessment)
{
    internal async Task<QuoteOperationResult<QuoteProofAccessGrant>> AuthorizeAsync(string quoteId,bool submit,QuoteRequestMetadata metadata,CancellationToken ct,bool content=false)
    {
        var grants=new List<QuoteAccess>();
        var requirements=new[]{QuoteCapabilities.Read,QuoteCapabilities.ReadAcceptanceEvidence}.ToList();
        if(submit) requirements.Add(QuoteCapabilities.SubmitAcceptanceEvidence);
        if(content) requirements.Add(QuoteCapabilities.ReadAcceptanceProofContent);
        foreach(var cap in requirements)
        {var a=await authorization.AuthorizeAsync(metadata,ct,cap); if(!a.IsSuccess)return QuoteOperationResult<QuoteProofAccessGrant>.Failure(a.Error!);grants.Add(a.Value!);}
        if(!QuotePricing.EntityId(quoteId))return QuoteOperationResult<QuoteProofAccessGrant>.Failure(QuoteErrors.NotFound());
        var quote=await quotes.ReadQuoteAsync(grants[0].Trusted.WorkspaceId,quoteId,ct);
        if(quote is null)return QuoteOperationResult<QuoteProofAccessGrant>.Failure(QuoteErrors.NotFound());
        foreach(var g in grants)
            if(await authorization.EnforceRecordAsync(g,quote,"quoteAcceptanceProof",metadata,ct) is {} error)return QuoteOperationResult<QuoteProofAccessGrant>.Failure(error);
        if(QuoteDocumentCommands.RenderFields.Concat(QuoteDeliveryCommands.ReadFields).Concat(["status","archivedAt","customerDecisionHistory"])
            .Any(f=>grants.Any(g=>!g.Authorization.CanRead(f))))return QuoteOperationResult<QuoteProofAccessGrant>.Failure(QuoteErrors.AccessDenied());
        return QuoteOperationResult<QuoteProofAccessGrant>.Success(new(quote,grants));
    }
    internal async Task<QuoteOperationError?> StoredAsync(QuoteProofAccessGrant grant,QuoteAcceptanceCase row,QuoteRequestMetadata metadata,CancellationToken ct)
    {
        var d=await documents.FindAsync(row.WorkspaceId,row.QuoteId,row.DocumentId,ct);
        if(d is null || d.QuoteRevision!=row.QuoteRevision || d.RootQuoteId!=row.RootQuoteId || d.PdfSha256!=row.IssuedPdfSha256 || d.CommercialFingerprint!=row.CommercialFingerprint)
            return new("QUOTE_PROOF_STORAGE_BLOCKED",409,"Stored binding unavailable");
        return await documentAccess.CanReadStoredAsync(d,grant.Grants,metadata,ct)?null:QuoteErrors.AccessDenied();
    }
    internal async Task<QuoteOperationResult<QuoteAcceptanceDocumentBinding>> CollectionAsync(string quoteId,string documentId,QuoteRequestMetadata metadata,CancellationToken ct)
    {
        var a=await assessment.ReadAsync(quoteId,documentId,metadata,ct);
        if(!a.IsSuccess)return QuoteOperationResult<QuoteAcceptanceDocumentBinding>.Failure(a.Error!);
        var ignored=new[]{"BLOCKED_CONTRACT_IDENTITY_AUTHORITY_SOURCE","BLOCKED_CONTRACT_TRUSTED_SCANNER","BLOCKED_CONTRACT_ACCEPTANCE_PROOF_STORAGE",
            "LINKED_ACCEPTANCE_BLOCKED_GATE_C","DOCUMENTARY_VALUE_LIMIT_OR_CURRENCY"};
        var blocker=a.Value!.BlockerCodes.FirstOrDefault(x=>!ignored.Contains(x,StringComparer.Ordinal));
        if(blocker is not null || a.Value.Binding is null)return QuoteOperationResult<QuoteAcceptanceDocumentBinding>.Failure(new("QUOTE_PROOF_STORAGE_BLOCKED",409,"Proof collection blocked",blocker??"ISSUED_DOCUMENT_SELECTION_REQUIRED"));
        return QuoteOperationResult<QuoteAcceptanceDocumentBinding>.Success(a.Value.Binding);
    }
}
