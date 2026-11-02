using System.Globalization;
using System.Text.Json;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed class QuoteIdentityEvaluation(IQuoteIdentityPersistence store,QuoteProofEffectiveScan scans,IQuoteAcceptancePartyReader parties,
    IQuoteIdentityPolicyReader policies,IQuoteVerifiedAcceptancePolicyReader verification,TimeProvider clock,QuoteManualSourceWorkflow manual)
{
    internal static T Decode<T>(string json)=>JsonSerializer.Deserialize<T>(json,QuotePricing.Json)??throw new InvalidOperationException("Stored identity evidence unavailable.");
    internal async Task<QuoteOperationResult<QuoteIdentityCaseView>> ViewAsync(Quote q,QuoteAcceptanceCase c,QuoteRequestMetadata meta,CancellationToken ct,bool verifyDocumentBytes=true)
    {
        var row=await store.CurrentAsync(q.WorkspaceId,c.CaseId,ct);var stored=row is null?null:Decode<QuoteIdentityCaseView>(row.SnapshotJson);
        var assessed=await AssessAsync(q,c,stored?.Binding,meta,ct,sourceCheck:stored?.LastSourceCheck,verifyDocumentBytes:verifyDocumentBytes);if(!assessed.IsSuccess)return QuoteOperationResult<QuoteIdentityCaseView>.Failure(assessed.Error!);
        return QuoteOperationResult<QuoteIdentityCaseView>.Success(new(c.CaseId,c.QuoteId,row?.ResourceVersion??0,new(c.BuyerType,c.BuyerId),stored?.Binding,assessed.Value!,stored?.LastSourceCheck));
    }
    internal async Task<QuoteOperationResult<QuoteIdentityAssessment>> AssessAsync(Quote q,QuoteAcceptanceCase c,QuoteIdentityBinding? binding,QuoteRequestMetadata meta,CancellationToken ct,QuoteAcceptancePartySnapshot? heldParty=null,QuoteIdentitySourceCheck? sourceCheck=null,bool verifyDocumentBytes=true)
    {
        var now=clock.GetUtcNow();var reasons=new List<string>();var policy=policies.Read(q.WorkspaceId);var workflow=verification.Read(q.WorkspaceId);
        if(policy is null)reasons.Add("IDENTITY_POLICY_MISSING_OR_INVALID");
        if(workflow is null)reasons.Add("VERIFICATION_POLICY_MISSING_OR_INVALID");
        else if(!QuoteVerifiedAcceptanceRules.WithinValueLimit(workflow,q.Currency,q.GrandTotalAmount))reasons.Add("DOCUMENTARY_VALUE_LIMIT_OR_CURRENCY");
        var sourceStatus="MISSING";QuoteEvidenceSource? sourceDescriptor=null;
        if(binding is null)reasons.Add("IDENTITY_BINDING_REQUIRED");
        else
        {
            await using var party=heldParty is null?await parties.AcquireAsync(c.BuyerType,c.BuyerId,binding.Party.ContactId,binding.Party.RelationshipId,meta.RequestId,meta.CorrelationId,ct):null;
            var current=heldParty??party?.Snapshot;if(current is null || current.WorkspaceId!=q.WorkspaceId)return QuoteOperationResult<QuoteIdentityAssessment>.Failure(QuoteErrors.AccessDenied());
            if(!current.Eligible)reasons.Add("CRM_PARTY_OR_AFFILIATION_INACTIVE");
            if(current.ContactVersion!=binding.Party.ContactVersion || current.BuyerVersion!=binding.Party.BuyerVersion || current.RelationshipId!=binding.Party.RelationshipId)reasons.Add("CRM_PARTY_SNAPSHOT_STALE");
            if(c.CommercialFingerprint!=QuoteApprovalAssessment.ContentFingerprint(q) || c.QuoteRevision!=q.QuoteRevision || c.BuyerType!=q.BuyerType || c.BuyerId!=q.BuyerId)reasons.Add("CASE_COMMERCIAL_BINDING_STALE");
            if(binding.IdentityProofs.Count==0)reasons.Add("IDENTITY_PROOF_REQUIRED");
            if(binding.AuthorityProofs.Count==0)reasons.Add("AUTHORITY_PROOF_REQUIRED");
            var links=binding.IdentityProofs.Concat(binding.AuthorityProofs).DistinctBy(x=>x.ProofId).ToArray();
            var proofs=await store.ProofsAsync(q.WorkspaceId,c.CaseId,links.Select(x=>x.ProofId).ToArray(),ct);
            var effective=await scans.ReadAsync(q.WorkspaceId,proofs,false,ct);
            if(links.Any(l=>!proofs.Any(p=>p.ProofId==l.ProofId && p.Sha256==l.Sha256 && p.EvidenceType==l.EvidenceType) || !effective.TryGetValue(l.ProofId,out var s)||!s.Reviewable))reasons.Add("PROOF_EFFECTIVE_CLEAN_REQUIRED");
            if(binding.SourceId is null)reasons.Add("APPROVED_SOURCE_REQUIRED");
            else
            {
                var source=await store.SourceAsync(q.WorkspaceId,binding.SourceId,ct);
                if(source is null || source.BuyerType!=c.BuyerType || !string.Equals(source.BuyerId,c.BuyerId,StringComparison.OrdinalIgnoreCase))reasons.Add("SOURCE_BUYER_BINDING_INVALID");
                else
                {
                    sourceStatus=source.Status;sourceDescriptor=Decode<QuoteEvidenceSource>(source.SnapshotJson);
                    if(source.ResourceVersion!=binding.SourceVersion)reasons.Add("SOURCE_VERSION_CHANGED");
                    if(source.Status!="APPROVED")reasons.Add(source.Status=="REVOKED"?"SOURCE_REVOKED":"SOURCE_NOT_APPROVED");
                }
            }
            var claim=binding.AuthorityClaim;
            if(!TryTime(claim.EffectiveFrom,out var from)||!TryTime(claim.EffectiveUntil,out var until))reasons.Add("AUTHORITY_VALIDITY_UNSPECIFIED");
            else if(!QuoteIdentityAuthorityRules.IsEffective(from,until,false,now))reasons.Add("CLAIMED_AUTHORITY_OUTSIDE_VALIDITY");
            if(claim.AmountLimit is null || !decimal.TryParse(claim.AmountLimit,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var amount))reasons.Add("AUTHORITY_LIMIT_UNSPECIFIED");
            else if(workflow is not null && !QuoteIdentityAuthorityRules.CoversAmount(q.Currency,q.GrandTotalAmount,claim.Currency,amount,workflow.CurrencyLimits))reasons.Add("CLAIMED_AUTHORITY_SCOPE_LIMIT");
            if(string.IsNullOrWhiteSpace(claim.ScopeDescription))reasons.Add("AUTHORITY_SCOPE_UNSPECIFIED");
        }
        if(binding is not null && manual.Enabled(q.WorkspaceId))reasons.AddRange(await manual.ReadinessAsync(q,c,binding,sourceDescriptor,sourceCheck,policy??"MISSING",ct,verifyDocumentBytes));
        else{reasons.Add("BLOCKED_CONTRACT_IDENTITY_AUTHORITY_SOURCE");reasons.Add("SOURCE_CHECK_REQUIRED");}
        var ready=reasons.Count==0;
        var invalid=reasons.Any(x=>x is "SOURCE_REVOKED" or "CLAIMED_AUTHORITY_OUTSIDE_VALIDITY" or "CLAIMED_AUTHORITY_SCOPE_LIMIT");
        return QuoteOperationResult<QuoteIdentityAssessment>.Success(new(ready?"READY_FOR_INDEPENDENT_REVIEW":invalid?"AUTHORITY_INVALID":"INSUFFICIENT_EVIDENCE",
            binding?.IdentityProofs.Count>0?"DOCUMENT_SUBMITTED":"MISSING",binding?.AuthorityProofs.Count>0?"DOCUMENT_SUBMITTED":"MISSING",sourceStatus,
            reasons.Distinct(StringComparer.Ordinal).ToArray(),now.UtcDateTime.ToString("O"),policy??"MISSING",ready,manual.Enabled(q.WorkspaceId)?QuoteManualSourceWorkflow.Simulation:"UNVERIFIED",false));
    }
    internal static bool TryTime(string? value,out DateTimeOffset result)
    {result=default;return value is {Length:>=20 and <=40} && value.EndsWith('Z') && DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out result);}
}
