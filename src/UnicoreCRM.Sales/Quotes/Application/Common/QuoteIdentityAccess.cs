using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed record QuoteIdentityContext(QuoteProofAccessGrant Grant,QuoteAcceptanceCase? Case,string? PolicyVersion);
internal sealed class QuoteIdentityAccess(QuoteProofAccess proofs,QuoteAuthorization authorization,IQuoteProofPersistence storage,IQuoteIdentityPolicyReader policies)
{
    internal async Task<QuoteOperationResult<QuoteIdentityContext>> AuthorizeAsync(string quoteId,string? caseId,QuoteRequestMetadata meta,CancellationToken ct,
        AccessRequirement? capability=null,bool submit=false,bool content=false,bool write=false)
    {
        var a=await proofs.AuthorizeAsync(quoteId,submit,meta,ct,content);if(!a.IsSuccess)return Fail(a.Error!);var grant=a.Value!;
        if(capability is not null)
        {
            var extra=await authorization.AuthorizeAsync(meta,ct,capability);if(!extra.IsSuccess)return Fail(extra.Error!);
            if(await authorization.EnforceRecordAsync(extra.Value!,grant.Quote,"quoteIdentityAuthority",meta,ct)is {} denied)return Fail(denied);
            if(QuoteDocumentCommands.RenderFields.Concat(QuoteDeliveryCommands.ReadFields).Concat(["status","archivedAt","customerDecisionHistory"]).Any(f=>!extra.Value!.Authorization.CanRead(f)))return Fail(QuoteErrors.AccessDenied());
            grant=new(grant.Quote,grant.Grants.Append(extra.Value!).ToArray());
        }
        var policy=policies.Read(grant.Quote.WorkspaceId);if(write && policy is null)return Fail(Block("IDENTITY_POLICY_MISSING_OR_INVALID"));
        QuoteAcceptanceCase? row=null;
        if(caseId is not null)
        {
            row=await storage.CaseAsync(grant.Quote.WorkspaceId,grant.Quote.QuoteId,caseId,false,ct);if(row is null)return Fail(QuoteErrors.NotFound());
            if(await proofs.StoredAsync(grant,row,meta,ct)is {} denied)return Fail(denied);
            if(write)
            {
                var b=await proofs.CollectionAsync(grant.Quote.QuoteId,row.DocumentId,meta,ct);if(!b.IsSuccess)return Fail(b.Error!);
                if(b.Value!.RootQuoteId!=row.RootQuoteId || b.Value.QuoteRevision!=row.QuoteRevision || b.Value.BuyerRef.Type!=row.BuyerType
                    || b.Value.BuyerRef.Id!=row.BuyerId || b.Value.PdfSha256!=row.IssuedPdfSha256 || b.Value.CommercialContentFingerprint!=row.CommercialFingerprint)
                    return Fail(Block("CASE_COMMERCIAL_BINDING_STALE"));
            }
        }
        return QuoteOperationResult<QuoteIdentityContext>.Success(new(grant,row,policy));
    }
    internal static QuoteOperationError Block(string reason)=>new("QUOTE_IDENTITY_AUTHORITY_BLOCKED",409,"Identity or authority evidence is blocked",reason);
    internal static QuoteOperationError Input()=>new("IDENTITY_EVIDENCE_INPUT_INVALID",422,"Invalid identity or authority evidence input");
    internal static QuoteOperationError Stale()=>new("VERSION_CONFLICT",412,"Evidence resource version conflict");
    private static QuoteOperationResult<QuoteIdentityContext> Fail(QuoteOperationError e)=>QuoteOperationResult<QuoteIdentityContext>.Failure(e);
}
