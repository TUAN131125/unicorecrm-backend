using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;

namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed class QuoteSalesAcceptanceService(QuoteAuthorization authorization,IQuotesPersistence quotes,
    IQuoteSalesAcceptanceStore store,IQuoteDocumentPersistence documents,QuoteDocumentCommands documentAccess,
    QuoteSalesPolicyReader policies,IQuoteApprovalPolicyReader approvalPolicies,IQuoteWorkspacePolicyReader workspaces,
    IWorkspaceTimeZoneReader zones,IDataProtectionProvider protection,IQuoteGmailSender gmail,TimeProvider clock,
    IQuoteProofPersistence proofs,QuoteProofEffectiveScan scans)
{
    internal const string TermsVersion="ordinary-sales-consent-v1";
    private readonly IDataProtector protector=protection.CreateProtector("QuoteSalesAcceptance.Delivery.v1");
    internal async Task<(QuoteActionAvailabilityReadModel Gmail,QuoteActionAvailabilityReadModel Staff,QuoteActionAvailabilityReadModel Link)> AvailabilityAsync(
        Quote q,QuoteRequestMetadata metadata,CancellationToken ct)
    {
        static QuoteActionAvailabilityReadModel Hint(string? code)=>new(code is null,code is null?[]:[code]);
        var command=new QuoteCommandMetadata(metadata.RequestId,metadata.CorrelationId,"availability",q.ResourceVersion);
        var sending=await StaffAccessAsync(q.QuoteId,command,ct,deliveryOnly:true);
        var accepting=await StaffAccessAsync(q.QuoteId,command,ct);
        var issued=(await documents.ListAsync(q.WorkspaceId,q.QuoteId,0,100,ct))
            .FirstOrDefault(d=>d.QuoteRevision==q.QuoteRevision && d.CommercialFingerprint==QuoteApprovalAssessment.ContentFingerprint(q));
        async Task<string?> Check(QuoteOperationResult<QuoteAccess> access,string source,bool deliveryOnly=false)
        {
            if(!access.IsSuccess)return "ACCESS_DENIED";
            if(issued is null)return "ACCEPTANCE_DOCUMENT_BINDING_INVALID";
            if(!await documentAccess.CanReadStoredAsync(issued,[access.Value!],metadata,ct))return "ACCESS_DENIED";
            return (await EligibleAsync(q,issued.DocumentId,source,ct,deliveryOnly,deliveryOnly))?.Code;
        }
        var staff=await Check(accepting,"STAFF_RECORDED");
        var link=await Check(accepting,"CUSTOMER_LINK");
        var send=await Check(sending,"CUSTOMER_LINK",true);
        var recipient=string.IsNullOrWhiteSpace(q.RecipientEmail) || !System.Net.Mail.MailAddress.TryCreate(q.RecipientEmail,out _)?"QUOTE_RECIPIENT_REQUIRED":null;
        var includesLink=issued is not null && await EligibleAsync(q,issued.DocumentId,"CUSTOMER_LINK",ct,true) is null;
        var linkConfiguration=policies.PublicBaseUrl is null?"ACCEPTANCE_PUBLIC_ORIGIN_UNAVAILABLE":null;
        send??=recipient??gmail.ConfigurationError(q.WorkspaceId)??(includesLink?linkConfiguration:null);
        if(send is null && await store.HasUnresolvedDeliveryAsync(q.WorkspaceId,q.QuoteId,ct))send="GMAIL_DELIVERY_UNRESOLVED";
        return(Hint(send),Hint(staff),Hint(link??linkConfiguration??recipient));
    }

    internal static QuoteSalesAcceptanceView View(QuoteSalesAcceptance a)=>new(a.AcceptanceId,a.QuoteId,a.Source,a.AssuranceLevel,
        a.AcceptedAt.UtcDateTime.ToString("O"),a.RecordedBy,a.Channel,a.Evidence,a.DocumentId,a.CommercialFingerprint,
        a.ConversionStatus,a.OrderId,a.LastErrorCode,a.OperationalApprovalRequired,a.AutoConfirmRequested);

    internal async Task<QuoteOperationResult<IReadOnlyList<QuoteInvitationView>>> HistoryAsync(string id,QuoteRequestMetadata metadata,CancellationToken ct)
    {
        var grant=await authorization.AuthorizeAsync(metadata,ct);if(!grant.IsSuccess)return Fail<IReadOnlyList<QuoteInvitationView>>(grant.Error!);
        var q=await quotes.ReadQuoteAsync(grant.Value!.Trusted.WorkspaceId,id,ct);if(q is null)return Fail<IReadOnlyList<QuoteInvitationView>>(QuoteErrors.NotFound());
        var denied=await authorization.EnforceRecordAsync(grant.Value,q,"listQuoteAcceptanceInvitations",metadata,ct);
        if(denied is not null)return Fail<IReadOnlyList<QuoteInvitationView>>(denied);
        if(new[]{"recipientEmail","deliveryHistory","customerDecisionHistory"}.Any(f=>!grant.Value.Authorization.CanRead(f)))return Fail<IReadOnlyList<QuoteInvitationView>>(QuoteErrors.AccessDenied());
        var rows=await store.InvitationsAsync(q.WorkspaceId,id,ct);
        quotes.AddReadAudit(new("listQuoteAcceptanceInvitations",q.WorkspaceId,grant.Value.Trusted.MemberId,id,metadata.RequestId,metadata.CorrelationId,q.ResourceVersion,clock.GetUtcNow()));
        await quotes.SaveChangesAsync(ct);return Ok<IReadOnlyList<QuoteInvitationView>>(rows.Select(i=>InvitationView(i,false)).ToArray());
    }

    internal async Task<QuoteOperationResult<QuoteSalesAcceptanceView>> StaffAsync(string id,StaffQuoteAcceptanceRequest request,QuoteCommandMetadata command,CancellationToken ct)
    {
        var fields=new Dictionary<string,string[]>();
        if(request.Channel is not ("PHONE" or "EMAIL" or "GMAIL" or "ZALO" or "IN_PERSON" or "OTHER"))fields["channel"]=["Choose a channel."];
        if(!request.ResponsibilityConfirmed)fields["responsibilityConfirmed"]=["Responsibility confirmation is required."];
        if(request.CustomerAgreedAt>clock.GetUtcNow() || request.CustomerAgreedAt<clock.GetUtcNow().AddYears(-1))fields["customerAgreedAt"]=["Invalid agreement time."];
        QuotePricing.Text(request.Evidence,"evidence",3500,true,fields);
        // Attachments use the existing quarantined B6 proof workflow; arbitrary client paths are never evidence.
        if(fields.Count>0)return Fail<QuoteSalesAcceptanceView>(QuoteErrors.Validation(fields));
        var grant=await StaffAccessAsync(id,command,ct); if(!grant.IsSuccess)return Fail<QuoteSalesAcceptanceView>(grant.Error!);
        var access=grant.Value!;
        await using var tx=await quotes.BeginAsync(ct);
        var scope=Scope(access.Trusted.WorkspaceId,access.Trusted.MemberId,"staff",command.IdempotencyKey);
        await quotes.LockAsync(scope,ct);await quotes.LockAsync($"aggregate:{access.Trusted.WorkspaceId}:{id}",ct);
        var q=await quotes.LoadQuoteAsync(access.Trusted.WorkspaceId,id,ct);if(q is null)return Fail<QuoteSalesAcceptanceView>(QuoteErrors.NotFound());
        var denied=await authorization.EnforceRecordAsync(access,q,"recordQuoteSalesAcceptance",new(command.RequestId,command.CorrelationId),ct);
        if(denied is not null)return Fail<QuoteSalesAcceptanceView>(denied);
        var fingerprint=Hash(new{id,request,command.ExpectedVersion});
        var replay=await quotes.FindCommandAsync(scope,ct);
        if(replay is not null)return replay.Fingerprint!=fingerprint?Error<QuoteSalesAcceptanceView>("IDEMPOTENCY_KEY_REUSED",409):
            Ok(View(q.SalesAcceptance??throw new InvalidOperationException("Missing acceptance receipt.")));
        if(q.ResourceVersion!=command.ExpectedVersion)return Error<QuoteSalesAcceptanceView>("VERSION_CONFLICT",412);
        var check=await EligibleAsync(q,request.DocumentId,"STAFF_RECORDED",ct);if(check is not null)return Fail<QuoteSalesAcceptanceView>(check);
        if(request.AttachmentEvidenceId is {} proofId)
        {
            var proofGrant=await authorization.AuthorizeAsync(new(command.RequestId,command.CorrelationId),ct,QuoteCapabilities.ReadAcceptanceEvidence);
            if(!proofGrant.IsSuccess || await authorization.EnforceRecordAsync(proofGrant.Value!,q,"staffAcceptanceAttachment",new(command.RequestId,command.CorrelationId),ct) is not null)return Fail<QuoteSalesAcceptanceView>(QuoteErrors.AccessDenied());
            var evidenceCase=await proofs.DocumentCaseAsync(q.WorkspaceId,q.QuoteId,request.DocumentId,ct);
            var proof=evidenceCase is null?null:await proofs.ProofAsync(q.WorkspaceId,evidenceCase.CaseId,proofId,ct);
            if(proof is null || !(await scans.ReadAsync(q.WorkspaceId,[proof],true,ct))[proof.ProofId].ContentAvailable)return Error<QuoteSalesAcceptanceView>("ATTACHMENT_NOT_AVAILABLE_OR_NOT_CLEAN",409);
        }
        var d=await documents.FindAsync(q.WorkspaceId,id,request.DocumentId,ct);
        if(d is null || !await documentAccess.CanReadStoredAsync(d,[access],new(command.RequestId,command.CorrelationId),ct))return Fail<QuoteSalesAcceptanceView>(QuoteErrors.AccessDenied());
        var buyerName=JsonSerializer.Deserialize<QuoteDocumentSnapshot>(d.SnapshotJson,QuotePricing.Json)!.Buyer.DisplayName;
        var a=CommitAcceptance(q,request.DocumentId,"STAFF_RECORDED",access.Trusted.MemberId,request.Channel,request.Evidence+(request.AttachmentEvidenceId is null?"":"\nProtected attachment: "+request.AttachmentEvidenceId),request.CustomerAgreedAt,command,scope,fingerprint,buyerName);
        await quotes.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Ok(View(a));
    }

    internal async Task<QuoteOperationResult<QuoteInvitationView>> InviteAsync(string id,QuoteInvitationRequest request,QuoteCommandMetadata command,CancellationToken ct)
    {
        var grant=await StaffAccessAsync(id,command,ct,deliveryOnly:request.SendGmail);if(!grant.IsSuccess)return Fail<QuoteInvitationView>(grant.Error!);
        var access=grant.Value!; var ws=access.Trusted.WorkspaceId;
        if(request.SendGmail && (!access.Authorization.CanWrite("sentAt") || !access.Authorization.CanWrite("deliveryHistory")))return Fail<QuoteInvitationView>(QuoteErrors.AccessDenied());
        await using var tx=await quotes.BeginAsync(ct);
        var scope=Scope(ws,access.Trusted.MemberId,"invitation",command.IdempotencyKey);
        await quotes.LockAsync(scope,ct);await quotes.LockAsync($"aggregate:{ws}:{id}",ct);
        var q=await quotes.LoadQuoteAsync(ws,id,ct);if(q is null)return Fail<QuoteInvitationView>(QuoteErrors.NotFound());
        var denied=await authorization.EnforceRecordAsync(access,q,"createQuoteAcceptanceInvitation",new(command.RequestId,command.CorrelationId),ct);
        if(denied is not null)return Fail<QuoteInvitationView>(denied);
        var fingerprint=Hash(new{id,request,command.ExpectedVersion});
        var replay=await quotes.FindCommandAsync(scope,ct);
        QuoteAcceptanceInvitation invitation; QuoteDocument document;
        if(replay is not null)
        {
            if(replay.Fingerprint!=fingerprint)return Error<QuoteInvitationView>("IDEMPOTENCY_KEY_REUSED",409);
            var receipt=JsonSerializer.Deserialize<QuoteInvitationView>(replay.ResponseJson,QuotePricing.Json)!;
            invitation=await store.InvitationByIdAsync(ws,id,receipt.InvitationId,ct)??throw new InvalidOperationException("Missing invitation receipt.");
            document=await documents.FindAsync(ws,id,invitation.DocumentId,ct)??throw new InvalidOperationException("Missing issued document.");
            if(!await documentAccess.CanReadStoredAsync(document,[access],new(command.RequestId,command.CorrelationId),ct))return Fail<QuoteInvitationView>(QuoteErrors.AccessDenied());
        }
        else
        {
            if(q.ResourceVersion!=command.ExpectedVersion)return Error<QuoteInvitationView>("VERSION_CONFLICT",412);
            if(request.SendGmail && await store.HasUnresolvedDeliveryAsync(ws,id,ct))return Error<QuoteInvitationView>("GMAIL_DELIVERY_UNRESOLVED",409);
            var error=await EligibleAsync(q,request.DocumentId,"CUSTOMER_LINK",ct,request.SendGmail,request.SendGmail);if(error is not null)return Fail<QuoteInvitationView>(error);
            document=(await documents.FindAsync(ws,id,request.DocumentId,ct))!;
            if(!await documentAccess.CanReadStoredAsync(document,[access],new(command.RequestId,command.CorrelationId),ct))return Fail<QuoteInvitationView>(QuoteErrors.AccessDenied());
            var includesLink=!request.SendGmail || await EligibleAsync(q,request.DocumentId,"CUSTOMER_LINK",ct,true) is null;
            var root=policies.PublicBaseUrl;
            if(includesLink && root is null)return Error<QuoteInvitationView>("ACCEPTANCE_PUBLIC_ORIGIN_UNAVAILABLE",503);
            if(string.IsNullOrWhiteSpace(q.RecipientEmail) || !System.Net.Mail.MailAddress.TryCreate(q.RecipientEmail,out _))return Error<QuoteInvitationView>("QUOTE_RECIPIENT_REQUIRED",422);
            var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var url=includesLink?root+"/#/quote-acceptance/"+token:"";
            var now=clock.GetUtcNow();var p=policies.Read(ws);
            invitation=new("quote_inv_"+Guid.NewGuid().ToString("N"),ws,id,document.DocumentId,q.ResourceVersion,
                QuoteApprovalAssessment.ContentFingerprint(q),QuoteApprovalAssessment.Hash(token),includesLink?protector.Protect(url):"",access.Trusted.MemberId,q.RecipientEmail,includesLink?now.AddHours(p!.InvitationHours):now,now);
            store.Add(invitation);
            quotes.AddCommand(new(scope,ws,access.Trusted.MemberId,"createQuoteAcceptanceInvitation",id,fingerprint,
                JsonSerializer.Serialize(InvitationView(invitation,false),QuotePricing.Json),"quote_audit_"+Guid.NewGuid().ToString("N"),command.RequestId,command.CorrelationId,q.ResourceVersion,q.ResourceVersion,now));
            await quotes.SaveChangesAsync(ct);
        }
        // A replay reads the durable outcome. It never sends again, including after a failure.
        // New keys cannot bypass an unresolved earlier attempt on this Quote.
        if(!request.SendGmail || invitation.DeliveryStatus!="NOT_SENT")
        {
            await tx.CommitAsync(ct);
            return Ok(InvitationView(invitation,!request.SendGmail));
        }
        if(await store.HasUnresolvedDeliveryAsync(ws,id,ct))return Error<QuoteInvitationView>("GMAIL_DELIVERY_UNRESOLVED",409);
        var pdf=await documents.ReadBytesAsync(ws,document.DocumentId,ct);
        if(pdf is null || Convert.ToHexString(SHA256.HashData(pdf))!=document.PdfSha256)return Error<QuoteInvitationView>("DOCUMENT_INTEGRITY_FAILED",503);
        var snapshot=JsonSerializer.Deserialize<QuoteDocumentSnapshot>(document.SnapshotJson,QuotePricing.Json)!;
        var body=$"Kính gửi {snapshot.Buyer.DisplayName},\nBáo giá {q.QuoteNumber}: {q.GrandTotalAmount.ToString(CultureInfo.InvariantCulture)} {q.Currency}.\nHiệu lực: {q.ValidUntil:yyyy-MM-dd}.";
        if(invitation.ProtectedDeliveryPayload.Length>0)
            body+=$"\nChấp nhận báo giá: {protector.Unprotect(invitation.ProtectedDeliveryPayload)}\nChỉ ghi nhận đồng ý khi bạn chủ động xác nhận trên trang. Liên kết không xác minh thẩm quyền đại diện pháp lý.";

        invitation.Delivery("SENDING",null,clock.GetUtcNow());
        await quotes.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await tx.DisposeAsync();
        // No SQL transaction or aggregate lock spans SMTP. A crash leaves SENDING durable and
        // blocks resending; provider Message-ID alone cannot provide exactly-once delivery.
        QuoteGmailResult outcome;
        try { outcome=await gmail.SendAsync(ws,invitation.RecipientEmail,"Báo giá "+q.QuoteNumber,body,pdf,q.QuoteNumber+".pdf",invitation.InvitationId,ct); }
        catch(Exception) { outcome=new("UNKNOWN","GMAIL_DELIVERY_OUTCOME_UNKNOWN"); }
        // Persist a known outcome even if the HTTP client disconnected while SMTP was in flight.
        using var completion=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var finish=completion.Token;
        await using var delivery=await quotes.BeginAsync(finish);
        await quotes.LockAsync($"aggregate:{ws}:{id}",finish);
        await store.ReloadAsync(invitation,finish);
        await store.ReloadQuoteAsync(q,finish);
        invitation.Delivery(outcome.Status,outcome.ErrorCode,clock.GetUtcNow());
        if(outcome.Status=="SENT")
        {
            // The provider fact remains authoritative even if a concurrent command changed the Quote.
            // Never overwrite a newer commercial binding or force a terminal Quote back to SENT.
            if(q.ResourceVersion==invitation.QuoteVersion && QuoteApprovalAssessment.ContentFingerprint(q)==invitation.CommercialFingerprint
                && q.Status is ("DRAFT" or "REVIEW" or "SENT") && q.ArchivedAt is null)
            {
                var history=JsonSerializer.Deserialize<List<JsonElement>>(q.DeliveryHistoryJson??"[]",QuotePricing.Json)??[];
                history.Add(JsonSerializer.SerializeToElement(new QuoteDeliveryRecordReadModel(invitation.InvitationId,"GMAIL",clock.GetUtcNow().UtcDateTime.ToString("O"),invitation.CommercialFingerprint)
                {EvidenceType="PROVIDER_CONFIRMED_SENT",RecipientEmail=invitation.RecipientEmail,SentBy=access.Trusted.MemberId,
                    QuoteRevision=document.QuoteRevision,ResourceVersion=q.ResourceVersion+1,DocumentAuthority=document.DocumentId,
                    RecordedAt=clock.GetUtcNow().UtcDateTime.ToString("O")},QuotePricing.Json));
                if(!q.RecordDelivery(JsonSerializer.Serialize(history,QuotePricing.Json),clock.GetUtcNow()))throw new InvalidOperationException("Validated Quote refused delivery.");
                invitation.BindSentVersion(q.ResourceVersion);
            }
            else invitation.Delivery("SENT","GMAIL_ACCEPTED_BINDING_CHANGED",clock.GetUtcNow());
        }
        quotes.AddOutbox(new("quote_event_"+Guid.NewGuid().ToString("N"),ws,id,"QUOTE_GMAIL_DELIVERY_RESULT",command.CorrelationId,
            JsonSerializer.Serialize(new{invitation.InvitationId,invitation.DeliveryStatus,invitation.DeliveryErrorCode},QuotePricing.Json),clock.GetUtcNow()));
        await quotes.SaveChangesAsync(finish);
        await delivery.CommitAsync(finish);
        return Ok(InvitationView(invitation,!request.SendGmail));
    }

    internal async Task<QuoteOperationResult<CustomerQuotePreview>> PreviewAsync(string token,CancellationToken ct)
    {
        if(!ValidToken(token))return Error<CustomerQuotePreview>("ACCEPTANCE_LINK_INVALID",404);
        var i=await store.InvitationAsync(QuoteApprovalAssessment.Hash(token),ct);
        if(i is null || i.ProtectedDeliveryPayload.Length==0 || i.ExpiresAt<=clock.GetUtcNow())return Error<CustomerQuotePreview>("ACCEPTANCE_LINK_INVALID_OR_EXPIRED",410);
        var q=await quotes.ReadQuoteAsync(i.WorkspaceId,i.QuoteId,ct);
        if(q is null || q.ArchivedAt is not null)return Error<CustomerQuotePreview>("ACCEPTANCE_LINK_UNAVAILABLE",410);
        var check=await EligibleAsync(q,i.DocumentId,"CUSTOMER_LINK",ct);if(check is not null)return Fail<CustomerQuotePreview>(check);
        if(q.ResourceVersion!=i.QuoteVersion || QuoteApprovalAssessment.ContentFingerprint(q)!=i.CommercialFingerprint)return Error<CustomerQuotePreview>("ACCEPTANCE_BINDING_STALE",409);
        var d=(await documents.FindAsync(i.WorkspaceId,i.QuoteId,i.DocumentId,ct))!;
        var snapshot=JsonSerializer.Deserialize<QuoteDocumentSnapshot>(d.SnapshotJson,QuotePricing.Json)!;
        return Ok(new CustomerQuotePreview(q.QuoteNumber,snapshot.Buyer.DisplayName,q.QuoteRevision,d.DocumentVersion,
            QuotePricing.Money(q.GrandTotalAmount,q.Currency),snapshot.Quote.LineItems,snapshot.Quote.PaymentAgreement,
            q.ValidUntil?.ToString("yyyy-MM-dd"),TermsVersion,"Tôi đồng ý nội dung và điều khoản của đúng phiên bản báo giá được hiển thị. Xác nhận này không phải xác minh danh tính hoặc thẩm quyền đại diện pháp lý.",i.DeliveryStatus));
    }

    internal async Task<QuoteOperationResult<QuoteSalesAcceptanceView>> ConsentAsync(CustomerQuoteConsentRequest request,QuoteCommandMetadata command,CancellationToken ct)
    {
        if(!ValidToken(request.Token) || !request.Agree || request.TermsVersion!=TermsVersion)return Error<QuoteSalesAcceptanceView>("CONSENT_INPUT_INVALID",422);
        var hash=QuoteApprovalAssessment.Hash(request.Token);
        await using var tx=await quotes.BeginAsync(ct);await quotes.LockAsync("invitation:"+hash,ct);
        var i=await store.InvitationAsync(hash,ct);
        if(i is null || i.ProtectedDeliveryPayload.Length==0 || i.ExpiresAt<=clock.GetUtcNow())return Error<QuoteSalesAcceptanceView>("ACCEPTANCE_LINK_INVALID_OR_EXPIRED",410);
        await quotes.LockAsync($"aggregate:{i.WorkspaceId}:{i.QuoteId}",ct);
        var q=await quotes.LoadQuoteAsync(i.WorkspaceId,i.QuoteId,ct);if(q is null)return Error<QuoteSalesAcceptanceView>("ACCEPTANCE_LINK_UNAVAILABLE",410);
        var scope=Scope(i.WorkspaceId,i.InvitationId,"consent",command.IdempotencyKey);
        var fingerprint=Hash(new{hash,request.Agree,request.TermsVersion});
        var replay=await quotes.FindCommandAsync(scope,ct);
        if(replay is not null)return replay.Fingerprint!=fingerprint?Error<QuoteSalesAcceptanceView>("IDEMPOTENCY_KEY_REUSED",409):Ok(View(q.SalesAcceptance!));
        if(i.ConsentedAt is not null)return Error<QuoteSalesAcceptanceView>("ACCEPTANCE_LINK_ALREADY_USED",409);
        if(q.ResourceVersion!=i.QuoteVersion || QuoteApprovalAssessment.ContentFingerprint(q)!=i.CommercialFingerprint)return Error<QuoteSalesAcceptanceView>("ACCEPTANCE_BINDING_STALE",409);
        var check=await EligibleAsync(q,i.DocumentId,"CUSTOMER_LINK",ct);if(check is not null)return Fail<QuoteSalesAcceptanceView>(check);
        var document=await documents.FindAsync(i.WorkspaceId,i.QuoteId,i.DocumentId,ct);
        var buyerName=JsonSerializer.Deserialize<QuoteDocumentSnapshot>(document!.SnapshotJson,QuotePricing.Json)!.Buyer.DisplayName;
        var now=clock.GetUtcNow();var a=CommitAcceptance(q,i.DocumentId,"CUSTOMER_LINK","invitation-bearer:"+i.InvitationId,"CUSTOMER_LINK",
            "Explicit customer consent; "+TermsVersion+"; invitation "+i.InvitationId,now,command,scope,fingerprint,buyerName);
        i.Consent(now);await quotes.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Ok(View(a));
    }

    private QuoteSalesAcceptance CommitAcceptance(Quote q,string document,string source,string actor,string channel,string evidence,
        DateTimeOffset agreed,QuoteCommandMetadata command,string scope,string fingerprint,string buyerName)
    {
        var now=clock.GetUtcNow();var prior=q.ResourceVersion;var p=policies.Read(q.WorkspaceId)!;
        var snapshot=JsonSerializer.SerializeToNode(QuoteProjection.Document(q),QuotePricing.Json)!;
        snapshot["buyerNameSnapshot"]=buyerName;
        var a=new QuoteSalesAcceptance("quote_accept_"+Guid.NewGuid().ToString("N"),q.WorkspaceId,q.QuoteId,source,actor,channel,evidence,
            document,QuoteApprovalAssessment.ContentFingerprint(q),snapshot.ToJsonString(QuotePricing.Json),p.Version,
            source=="STAFF_RECORDED" && p.RequireStaffOperationalApproval,agreed,now,autoConfirmRequested:true);
        if(!q.AcceptSales(a,now))throw new InvalidOperationException("Acceptance state changed inside serialized command.");
        store.Add(a);var audit="quote_audit_"+Guid.NewGuid().ToString("N");var eventId="quote_event_"+Guid.NewGuid().ToString("N");
        quotes.AddCommand(new(scope,q.WorkspaceId,actor,source=="CUSTOMER_LINK"?"consentCustomerQuoteAcceptance":"recordQuoteSalesAcceptance",q.QuoteId,fingerprint,
            JsonSerializer.Serialize(View(a),QuotePricing.Json),audit,command.RequestId,command.CorrelationId,prior,q.ResourceVersion,now));
        quotes.AddOutbox(new(eventId,q.WorkspaceId,q.QuoteId,"QUOTE_SALES_ACCEPTED_ORDER_REQUESTED",command.CorrelationId,
            JsonSerializer.Serialize(new{a.AcceptanceId,a.Source,a.AssuranceLevel,a.CommercialFingerprint,actor,a.PolicyVersion,a.DocumentId,agreedAt=agreed},QuotePricing.Json),now));
        return a;
    }
    private async Task<QuoteOperationError?> EligibleAsync(Quote q,string document,string source,CancellationToken ct,bool allowUnsent=false,bool deliveryOnly=false)
    {
        // Retained constructor compatibility for historical consumers; B1 is not an issuance prerequisite.
        _ = approvalPolicies;
        var p=policies.Read(q.WorkspaceId);
        if(!deliveryOnly)
        {
        if(p is null || source=="STAFF_RECORDED" && !p.AllowStaffRecorded || source=="CUSTOMER_LINK" && !p.AllowCustomerLink)
            return new("ACCEPTANCE_POLICY_DISABLED",409,"Workspace acceptance policy does not allow this source");
        if(p.RequireVerifiedSignerAuthority)return new("VERIFIED_SIGNER_AUTHORITY_REQUIRED",409,"Verified signer authority is required");
        if(!p.OrderDraftLimits.TryGetValue(q.Currency,out var max) || max<=0 || q.GrandTotalAmount<=0 || q.GrandTotalAmount>max)
            return new("ACCEPTANCE_RISK_LIMIT_EXCEEDED",409,"Quote exceeds the ordinary-sales policy");
        }
        if((allowUnsent?q.Status is not ("DRAFT" or "REVIEW" or "SENT"):q.Status!="SENT") || q.ArchivedAt is not null || q.SalesAcceptance is not null)return new("QUOTE_ACCEPTANCE_STATE_INVALID",409,"Quote cannot be accepted");
        if((await workspaces.ReadAsync(q.WorkspaceId,ct)) is not {Enabled:true})return QuoteErrors.AccessDenied();
        var validity=QuoteValidity.Evaluate(q.ValidUntil,await zones.ReadTimeZoneAsync(q.WorkspaceId,ct),clock.GetUtcNow());
        if(validity.State is "OVERDUE" or "TIMEZONE_UNAVAILABLE")return new("QUOTE_EXPIRED_OR_TIMEZONE_UNAVAILABLE",409,"Quote validity cannot be established");
        var commercialFingerprint=QuoteApprovalAssessment.ContentFingerprint(q);
        if(!QuoteRevisionContent.Build(q,q.QuoteId).IsSuccess || !deliveryOnly && q.PaymentAgreementJson is null)return new("QUOTE_COMMERCIAL_SNAPSHOT_INVALID",409,"Commercial snapshot invalid");
        var d=await documents.FindAsync(q.WorkspaceId,q.QuoteId,document,ct);
        if(d is null || d.QuoteRevision!=q.QuoteRevision || d.CommercialFingerprint!=commercialFingerprint)return new("ACCEPTANCE_DOCUMENT_BINDING_INVALID",409,"Issued document binding invalid");
        var bytes=await documents.ReadBytesAsync(q.WorkspaceId,document,ct);
        return bytes is null || bytes.Length!=d.FileSize || Convert.ToHexString(SHA256.HashData(bytes))!=d.PdfSha256
            ? new("DOCUMENT_INTEGRITY_FAILED",409,"Issued document integrity invalid"):null;
    }
    private async Task<QuoteOperationResult<QuoteAccess>> StaffAccessAsync(string id,QuoteCommandMetadata command,CancellationToken ct,bool deliveryOnly=false)
    {
        var metadata=new QuoteRequestMetadata(command.RequestId,command.CorrelationId);
        if(!QuotePricing.EntityId(id) || command.ExpectedVersion is null)return Fail<QuoteAccess>(QuoteErrors.Validation(new Dictionary<string,string[]>{{"If-Match",["A resource version is required."]}}));
        QuoteAccess? result=null;
        foreach(var capability in deliveryOnly?new[]{QuoteCapabilities.Read,QuoteCapabilities.Update}:new[]{QuoteCapabilities.Read,QuoteCapabilities.Update,AccessRequirement.ForCanonicalCapability("orders.create")})
        {
            var grant=await authorization.AuthorizeAsync(metadata,ct,capability);if(!grant.IsSuccess)return grant;
            var q=await quotes.ReadQuoteAsync(grant.Value!.Trusted.WorkspaceId,id,ct);if(q is null)return Fail<QuoteAccess>(QuoteErrors.NotFound());
            var denied=await authorization.EnforceRecordAsync(grant.Value,q,"quoteSalesAcceptance",metadata,ct);if(denied is not null)return Fail<QuoteAccess>(denied);
            var readFields=deliveryOnly?QuoteDeliveryCommands.ReadFields.Concat(["status"]):QuoteDeliveryCommands.ReadFields.Concat(["customerDecisionHistory","salesAcceptance","status","acceptedAt"]);
            var writeFields=deliveryOnly?new[]{"status","sentAt","deliveryHistory"}:new[]{"status","acceptedAt","customerDecisionHistory","salesAcceptance"};
            if(readFields.Any(f=>!grant.Value.Authorization.CanRead(f)) || writeFields.Any(f=>!grant.Value.Authorization.CanWrite(f)))
                return Fail<QuoteAccess>(QuoteErrors.AccessDenied());
            result=grant.Value;
        }
        return Ok(result!);
    }
    private QuoteInvitationView InvitationView(QuoteAcceptanceInvitation i,bool disclose)=>new(i.InvitationId,i.QuoteId,
        i.DeliveryStatus=="FAILED" && i.DeliveryErrorCode is ("GMAIL_DELIVERY_TIMEOUT" or "GMAIL_DELIVERY_FAILED")?"UNKNOWN":i.DeliveryStatus,i.DeliveryErrorCode,
        i.ExpiresAt.UtcDateTime.ToString("O"),disclose && i.ProtectedDeliveryPayload.Length>0?protector.Unprotect(i.ProtectedDeliveryPayload):null,
        i.CreatedAt.UtcDateTime.ToString("O"),i.CreatedBy,i.DeliveredAt?.UtcDateTime.ToString("O"),i.ProtectedDeliveryPayload.Length>0);
    private static bool ValidToken(string token)=>token is {Length:64} && token.All(Uri.IsHexDigit);
    private static string Scope(string ws,string actor,string op,string key)=>QuoteApprovalAssessment.Hash($"sales-acceptance|{ws}|{actor}|{op}|{key}");
    private static string Hash<T>(T value)=>QuoteApprovalAssessment.Hash(JsonSerializer.Serialize(value,QuotePricing.Json));
    private static QuoteOperationResult<T> Ok<T>(T result)=>QuoteOperationResult<T>.Success(result);
    private static QuoteOperationResult<T> Fail<T>(QuoteOperationError error)=>QuoteOperationResult<T>.Failure(error);
    private static QuoteOperationResult<T> Error<T>(string code,int status)=>Fail<T>(new(code,status,code));
}
