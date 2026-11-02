using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed class QuoteProofCommands(QuoteProofAccess access,IQuotesPersistence quotes,IQuoteProofPersistence proofs,
    IQuoteProofPolicyReader policies,IQuoteProofCipher cipher,TimeProvider clock,IQuoteProofCommitProbe? probe=null)
{
    private static readonly SemaphoreSlim UploadSlots=new(4,4);
    internal async Task<QuoteOperationResult<QuoteProofMutation>> CreateAsync(string quoteId,CreateQuoteAcceptanceCaseRequest request,QuoteCommandMetadata command,CancellationToken ct)
    {
        var meta=new QuoteRequestMetadata(command.RequestId,command.CorrelationId);
        var grant=await access.AuthorizeAsync(quoteId,true,meta,ct);if(!grant.IsSuccess)return Fail(grant.Error!);
        var w=grant.Value!.Quote.WorkspaceId;
        if(!QuotePricing.EntityId(request.DocumentId) || request.QuoteRevision<1 || !Hash(request.PdfSha256) || !Hash(request.CommercialContentFingerprint))return Fail(Input());
        var scope=Scope(w,grant.Value.Grants[0].Trusted.MemberId,"createQuoteAcceptanceCase",quoteId,command.IdempotencyKey);
        var fingerprint=QuoteApprovalAssessment.Hash(JsonSerializer.Serialize(new{request,command.ExpectedVersion},QuotePricing.Json));
        await using var tx=await quotes.BeginAsync(ct);
        await quotes.LockAsync(scope,ct);await quotes.LockAsync("proof-quota:"+w,ct);await quotes.LockAsync($"aggregate:{w}:{quoteId}",ct);
        grant=await access.AuthorizeAsync(quoteId,true,meta,ct);if(!grant.IsSuccess)return Fail(grant.Error!);
        var replay=await ReplayAsync(scope,fingerprint,grant.Value!,meta,ct);if(replay is not null)return replay;
        if(grant.Value!.Quote.ResourceVersion!=command.ExpectedVersion)return Fail(Stale());
        var policy=policies.Read(w);if(policy is null)return Fail(Block("STORAGE_POLICY_MISSING_OR_INVALID"));
        var bind=await access.CollectionAsync(quoteId,request.DocumentId,meta,ct);if(!bind.IsSuccess)return Fail(bind.Error!);
        var b=bind.Value!;
        if(request.QuoteRevision!=b.QuoteRevision || !string.Equals(request.PdfSha256,b.PdfSha256,StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.CommercialContentFingerprint,b.CommercialContentFingerprint,StringComparison.OrdinalIgnoreCase))return Fail(Block("QUOTE_ACCEPTANCE_BINDING_MISMATCH"));
        if(await proofs.DocumentCaseAsync(w,quoteId,b.DocumentId,ct) is not null)return Fail(Block("CASE_ALREADY_EXISTS"));
        var usage=await proofs.UsageAsync(w,null,ct);if(usage.CaseCount>=policy.WorkspaceCases)return Fail(Quota());
        var now=clock.GetUtcNow();var row=new QuoteAcceptanceCase(w,"qcase_"+Guid.NewGuid().ToString("N"),b.QuoteId,b.RootQuoteId,b.QuoteRevision,b.DocumentId,
            b.CommercialContentFingerprint,b.PdfSha256,b.BuyerRef.Type,b.BuyerRef.Id,policy.Version,grant.Value.Grants[0].Trusted.MemberId,now);
        proofs.AddCase(row);
        return await CommitAsync(row,null,scope,fingerprint,"createQuoteAcceptanceCase","QUOTE_ACCEPTANCE_CASE_CREATED",0,command,tx,ct);
    }
    internal async Task<QuoteOperationResult<QuoteProofMutation>> UploadAsync(string quoteId,string caseId,Stream body,long? length,string? mediaType,
        string? encodedName,string? evidenceType,QuoteCommandMetadata command,CancellationToken ct)
    {
        if(!await UploadSlots.WaitAsync(0,ct))return Fail(new("QUOTE_PROOF_STORAGE_BLOCKED",429,"Upload capacity temporarily unavailable"));
        try{return await UploadCoreAsync(quoteId,caseId,body,length,mediaType,encodedName,evidenceType,command,ct);}
        finally{UploadSlots.Release();}
    }
    private async Task<QuoteOperationResult<QuoteProofMutation>> UploadCoreAsync(string quoteId,string caseId,Stream body,long? length,string? mediaType,
        string? encodedName,string? evidenceType,QuoteCommandMetadata command,CancellationToken ct)
    {
        var meta=new QuoteRequestMetadata(command.RequestId,command.CorrelationId);
        var grant=await access.AuthorizeAsync(quoteId,true,meta,ct);if(!grant.IsSuccess)return Fail(grant.Error!);
        var w=grant.Value!.Quote.WorkspaceId;var row=await proofs.CaseAsync(w,quoteId,caseId,false,ct);
        if(row is null)return Fail(QuoteErrors.NotFound());
        if(await access.StoredAsync(grant.Value,row,meta,ct) is {} denied)return Fail(denied);
        var policy=policies.Read(w);if(policy is null)return Fail(Block("STORAGE_POLICY_MISSING_OR_INVALID"));
        if(!string.Equals(mediaType,"application/pdf",StringComparison.OrdinalIgnoreCase))return Fail(new("PROOF_INPUT_INVALID",415,"PDF media type required"));
        var name=QuoteProofInput.FileName(encodedName);
        if(name is null || !QuoteProofInput.EvidenceTypes.Contains(evidenceType,StringComparer.Ordinal))return Fail(Input());
        if(length>policy.FileBytes)return Fail(new("PROOF_INPUT_INVALID",413,"Proof exceeds size limit"));
        byte[] bytes;string hash;
        try
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(15));
            using var data=new MemoryStream();using var sha=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[65536];int count;
            while((count=await body.ReadAsync(buffer,deadline.Token))>0)
            {if(data.Length+count>policy.FileBytes)return Fail(new("PROOF_INPUT_INVALID",413,"Proof exceeds size limit"));sha.AppendData(buffer,0,count);await data.WriteAsync(buffer.AsMemory(0,count),deadline.Token);}
            bytes=data.ToArray();hash=Convert.ToHexString(sha.GetHashAndReset());
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){return Fail(new("PROOF_INPUT_INVALID",408,"Upload time limit exceeded"));}
        if(!QuoteProofInput.PdfCandidate(bytes))return Fail(Input());
        var actor=grant.Value.Grants[0].Trusted.MemberId;
        var scope=Scope(w,actor,"uploadQuoteAcceptanceProof",caseId,command.IdempotencyKey);
        var fingerprint=QuoteApprovalAssessment.Hash(JsonSerializer.Serialize(new{quoteId=grant.Value.Quote.QuoteId,caseId=row.CaseId,hash,size=bytes.Length,name,evidenceType,command.ExpectedVersion},QuotePricing.Json));
        await using var tx=await quotes.BeginAsync(ct);
        await quotes.LockAsync(scope,ct);await quotes.LockAsync("proof-quota:"+w,ct);await quotes.LockAsync($"aggregate:{w}:{quoteId}",ct);await quotes.LockAsync("proof-case:"+w+":"+row.CaseId,ct);
        grant=await access.AuthorizeAsync(quoteId,true,meta,ct);if(!grant.IsSuccess)return Fail(grant.Error!);
        row=await proofs.CaseAsync(w,quoteId,row.CaseId,true,ct);if(row is null)return Fail(QuoteErrors.NotFound());
        if(await access.StoredAsync(grant.Value!,row,meta,ct) is {} deniedAgain)return Fail(deniedAgain);
        var replay=await ReplayAsync(scope,fingerprint,grant.Value!,meta,ct);if(replay is not null)return replay;
        if(row.ResourceVersion!=command.ExpectedVersion)return Fail(Stale());
        policy=policies.Read(w);if(policy is null)return Fail(Block("STORAGE_POLICY_MISSING_OR_INVALID"));
        if(bytes.Length>policy.FileBytes)return Fail(new("PROOF_INPUT_INVALID",413,"Proof exceeds size limit"));
        var bind=await access.CollectionAsync(quoteId,row.DocumentId,meta,ct);if(!bind.IsSuccess)return Fail(bind.Error!);
        var b=bind.Value!;
        if(row.QuoteRevision!=b.QuoteRevision || row.RootQuoteId!=b.RootQuoteId || row.CommercialFingerprint!=b.CommercialContentFingerprint || row.IssuedPdfSha256!=b.PdfSha256
            || row.BuyerType!=b.BuyerRef.Type || row.BuyerId!=b.BuyerRef.Id)return Fail(Block("QUOTE_ACCEPTANCE_BINDING_MISMATCH"));
        if(await proofs.HasHashAsync(w,row.CaseId,hash,ct))return Fail(new("PROOF_ALREADY_EXISTS",409,"Proof already recorded in this case"));
        var usage=await proofs.UsageAsync(w,row.CaseId,ct);
        if(usage.WorkspaceFiles>=policy.WorkspaceFiles || usage.CaseFiles>=policy.CaseFiles || usage.WorkspaceBytes+bytes.Length>policy.WorkspaceBytes || usage.CaseBytes+bytes.Length>policy.CaseBytes)return Fail(Quota());
        var now=clock.GetUtcNow();var before=row.ResourceVersion;
        var proof=new QuoteAcceptanceProof(w,row.CaseId,"qproof_"+Guid.NewGuid().ToString("N"),evidenceType!,name,bytes.Length,hash,policy.Version,actor,now);
        var encrypted=cipher.Protect(w,row.CaseId,proof.ProofId,bytes);CryptographicOperations.ZeroMemory(bytes);
        proofs.AddProof(proof,encrypted);row.RecordQuarantine(now);
        return await CommitAsync(row,proof,scope,fingerprint,"uploadQuoteAcceptanceProof","QUOTE_ACCEPTANCE_PROOF_QUARANTINED",before,command,tx,ct);
    }
    private async Task<QuoteOperationResult<QuoteProofMutation>?> ReplayAsync(string scope,string fingerprint,QuoteProofAccessGrant grant,QuoteRequestMetadata meta,CancellationToken ct)
    {
        var receipt=await quotes.FindCommandAsync(scope,ct);if(receipt is null)return null;
        if(receipt.Fingerprint!=fingerprint)return Fail(new("IDEMPOTENCY_KEY_REUSED",409,"Idempotency key reused"));
        var response=JsonSerializer.Deserialize<QuoteProofMutation>(receipt.ResponseJson,QuotePricing.Json)??throw new InvalidOperationException("Invalid proof receipt.");
        var row=await proofs.CaseAsync(grant.Quote.WorkspaceId,grant.Quote.QuoteId,response.AggregateId,false,ct);
        if(row is null)return Fail(QuoteErrors.NotFound());
        if(await access.StoredAsync(grant,row,meta,ct) is {} denied)return Fail(denied);
        return QuoteOperationResult<QuoteProofMutation>.Success(response with{Outcome="REPLAYED",Result=response.Result with{Case=CaseMetadata(row)}});
    }
    private async Task<QuoteOperationResult<QuoteProofMutation>> CommitAsync(QuoteAcceptanceCase row,QuoteAcceptanceProof? proof,string scope,string fingerprint,
        string operation,string eventType,long before,QuoteCommandMetadata command,IQuoteTransaction tx,CancellationToken ct)
    {
        var now=clock.GetUtcNow();var id="quote_command_"+Guid.NewGuid().ToString("N");var audit="quote_audit_"+Guid.NewGuid().ToString("N");var evt="quote_event_"+Guid.NewGuid().ToString("N");
        var actor=proof?.UploadedBy??row.CreatedBy;
        var response=new QuoteProofMutation(id,command.CorrelationId,row.CaseId,"QUOTE_ACCEPTANCE_CASE",row.ResourceVersion,now.UtcDateTime.ToString("O",CultureInfo.InvariantCulture),"COMMITTED",
            new(CaseMetadata(row),proof is null?null:ProofMetadata(proof,row.QuoteId)),[evt],[audit]);
        quotes.AddCommand(new(scope,row.WorkspaceId,actor,operation,row.QuoteId,fingerprint,JsonSerializer.Serialize(response,QuotePricing.Json),audit,
            command.RequestId,command.CorrelationId,before,row.ResourceVersion,now));
        quotes.AddOutbox(new(evt,row.WorkspaceId,row.QuoteId,eventType,command.CorrelationId,JsonSerializer.Serialize(new{row.CaseId,row.DocumentId,row.QuoteRevision,
            row.ResourceVersion,expectedAggregateVersion=command.ExpectedVersion,proofId=proof?.ProofId,sha256=proof?.Sha256,fileSize=proof?.FileSize,storageStatus=proof?.StorageStatus,
            scanStatus=proof?.ScanStatus,policyVersion=proof?.PolicyVersion??row.PolicyVersion,actor},QuotePricing.Json),now));
        try{await quotes.SaveChangesAsync(ct);probe?.AfterStorageFlush(row.WorkspaceId);await tx.CommitAsync(ct);}
        catch(QuotePersistenceConcurrencyException){return Fail(Stale());}
        return QuoteOperationResult<QuoteProofMutation>.Success(response);
    }
    internal static QuoteAcceptanceCaseMetadata CaseMetadata(QuoteAcceptanceCase c)=>new(c.CaseId,c.QuoteId,c.RootQuoteId,c.QuoteRevision,c.DocumentId,c.CommercialFingerprint,c.IssuedPdfSha256,
        new(c.BuyerType,c.BuyerId),c.CaseStatus,c.CreatedAt.UtcDateTime.ToString("O",CultureInfo.InvariantCulture),c.CreatedBy,c.ResourceVersion);
    internal static QuoteAcceptanceProofMetadata ProofMetadata(QuoteAcceptanceProof p,string quoteId)=>new(p.ProofId,p.CaseId,quoteId,p.EvidenceType,p.FileName,p.MediaType,p.FileSize,p.Sha256,
        p.StorageStatus,p.ScanStatus,p.UploadedAt.UtcDateTime.ToString("O",CultureInfo.InvariantCulture),p.UploadedBy,p.ResourceVersion,p.Retention);
    private static string Scope(string w,string actor,string op,string aggregate,string key)=>QuoteApprovalAssessment.Hash($"{w}\n{actor}\n{op}\n{aggregate.ToLowerInvariant()}\n{key}");
    private static bool Hash(string? v)=>v is {Length:64} && v.All(Uri.IsHexDigit);
    private static QuoteOperationError Input()=>new("PROOF_INPUT_INVALID",422,"Invalid proof input");
    private static QuoteOperationError Block(string reason)=>new("QUOTE_PROOF_STORAGE_BLOCKED",409,"Proof collection blocked",reason);
    private static QuoteOperationError Quota()=>new("QUOTE_PROOF_QUOTA_EXCEEDED",409,"Proof storage quota exceeded");
    private static QuoteOperationError Stale()=>new("VERSION_CONFLICT",412,"Resource version conflict");
    private static QuoteOperationResult<QuoteProofMutation> Fail(QuoteOperationError e)=>QuoteOperationResult<QuoteProofMutation>.Failure(e);
}
