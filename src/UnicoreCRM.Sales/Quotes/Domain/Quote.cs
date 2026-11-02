namespace UnicoreCRM.Sales.Quotes.Domain;

/// <summary>
/// Quotes owns durable commercial snapshots and Draft content replacement.
/// </summary>
internal sealed class Quote
{
    private Quote() { }

    internal Quote(string workspaceId, string quoteId, string number, string buyerType, string buyerId,
        string sourcePath, string? dealId, string? contactId, string? leadId, QuoteDraftContent content, DateTimeOffset now)
    {
        WorkspaceId = workspaceId; QuoteId = quoteId; QuoteNumber = number;
        RootQuoteId = quoteId; QuoteRevision = 1; Status = "DRAFT";
        BuyerType = buyerType; BuyerId = buyerId; SourcePath = sourcePath;
        SourceDealId = dealId; ContactId = contactId; SourceLeadId = leadId;
        ActionsJson = "{\"accept\":{\"allowed\":false,\"blockerCodes\":[\"QUOTE_ACCEPTANCE_BLOCKED\"]}}";
        CreatedAt = now; ResourceVersion = 0;
        ReplaceDraft(content, now);
    }

    internal Quote? CreateRevision(string id, string number, int revision, QuoteDraftContent content, DateTimeOffset now)
    {
        if (ArchivedAt is not null || Status is not ("SENT" or "REJECTED" or "EXPIRED") || revision <= QuoteRevision
            || string.IsNullOrWhiteSpace(RootQuoteId)) return null;
        return new Quote(WorkspaceId, id, number, BuyerType, BuyerId, SourcePath, SourceDealId, ContactId, SourceLeadId, content, now)
        { RootQuoteId = RootQuoteId, RevisionOfQuoteId = QuoteId, QuoteRevision = revision,
            SenderName = SenderName, SenderAddress = SenderAddress, SenderEmail = SenderEmail, SenderTaxId = SenderTaxId };
    }

    internal bool RecordDocumentIssued(DateTimeOffset now)
    {
        if (ArchivedAt is not null || Status is not ("DRAFT" or "REVIEW" or "SENT")) return false;
        ResourceVersion++; UpdatedAt = now; return true;
    }

    internal bool Expire(DateOnly workspaceDate, DateTimeOffset now)
    {
        if (ArchivedAt is not null || Status != "SENT" || ValidUntil is null || workspaceDate <= ValidUntil.Value) return false;
        Status = "EXPIRED"; ExpiredAt = now; UpdatedAt = now; ResourceVersion++;
        return true;
    }

    internal bool Archive(string reason, DateTimeOffset now)
    {
        if (ArchivedAt is not null || Status is not ("DRAFT" or "REVIEW" or "REJECTED" or "EXPIRED")
            || string.IsNullOrWhiteSpace(reason) || reason.Length > 500) return false;
        ArchivedAt = now; ArchiveReason = reason; UpdatedAt = now; ResourceVersion++;
        return true;
    }

    internal bool ReplaceDraft(QuoteDraftContent content, DateTimeOffset now)
    {
        if (Status is not ("DRAFT" or "REVIEW") || ArchivedAt is not null) return false;
        Title = content.Title; Currency = content.Currency; OwnerId = content.OwnerId;
        RecipientEmail = content.RecipientEmail; LineItemsJson = content.LineItemsJson;
        AdjustmentsJson = content.AdjustmentsJson; ProductSnapshotsJson = content.ProductSnapshotsJson;
        SubtotalAmount = content.Subtotal; DiscountTotalAmount = content.Discount;
        TaxTotalAmount = content.Tax; GrandTotalAmount = content.Total;
        SubtotalCurrency = DiscountTotalCurrency = TaxTotalCurrency = GrandTotalCurrency = content.Currency;
        ValidUntil = content.ValidUntil; Notes = content.Notes; PaymentAgreementJson = content.PaymentAgreementJson;
        Status = "DRAFT";
        // Retired internal approval metadata remains historical; it never authorizes new issuance.
        UpdatedAt = now; ResourceVersion++;
        return true;
    }

    internal void RequestApproval(bool required, string reasonsJson, string fingerprint, string policyVersion,
        string actor, string? note, DateTimeOffset now)
    {
        ApprovalRequired = required; ApprovalReasonsJson = reasonsJson;
        ApprovalStatus = required ? "PENDING" : "NOT_REQUIRED";
        Status = required ? "REVIEW" : "DRAFT";
        ApprovalRequestedAt = now; ApprovalRequestedBy = actor; ReviewRequestedAt = required ? now : null;
        ApprovedAt = null; ApprovedBy = null; ApprovalDecisionNote = note;
        ApprovalContentFingerprint = fingerprint; ApprovalPolicyVersion = policyVersion;
        UpdatedAt = now; ResourceVersion++;
    }

    internal void DecideApproval(bool approve, string actor, string? note, DateTimeOffset now)
    {
        ApprovalStatus = approve ? "APPROVED" : "CHANGES_REQUESTED";
        ApprovedAt = approve ? now : null; ApprovedBy = approve ? actor : null;
        ApprovalDecisionNote = note; Status = approve ? "REVIEW" : "DRAFT";
        UpdatedAt = now; ResourceVersion++;
    }

    internal bool RecordDelivery(string historyJson, DateTimeOffset now)
    {
        if (Status is not ("DRAFT" or "REVIEW" or "SENT") || ArchivedAt is not null) return false;
        DeliveryHistoryJson = historyJson;
        Status = "SENT"; SentAt ??= now; UpdatedAt = now; ResourceVersion++;
        return true;
    }

    internal bool Reject(string reason, DateTimeOffset now, QuoteCustomerDecisionRecord decision)
    {
        if (Status != "SENT" || ArchivedAt is not null || string.IsNullOrWhiteSpace(reason) || reason.Length > 500) return false;
        if (decision.DecisionType != "REJECTION" || !AppendDecision(decision)) return false;
        Status = "REJECTED"; RejectedAt = now; UpdatedAt = now; ResourceVersion++;
        return true;
    }

    internal bool RecordReportedAcceptance(QuoteCustomerDecisionRecord decision, DateTimeOffset now)
    {
        if (Status != "SENT" || ArchivedAt is not null || decision.DecisionType != "ACCEPTANCE" || !AppendDecision(decision)) return false;
        UpdatedAt = now; ResourceVersion++;
        return true;
    }

    private bool AppendDecision(QuoteCustomerDecisionRecord decision)
    {
        if (decision.WorkspaceId != WorkspaceId || decision.QuoteId != QuoteId || decision.QuoteRevision != QuoteRevision
            || decision.ContentResourceVersion != ResourceVersion || decision.ResultResourceVersion != ResourceVersion + 1
            || decision.VerificationStatus != "UNVERIFIED" || CustomerDecisions.Any(x => x.DecisionId == decision.DecisionId)) return false;
        CustomerDecisions.Add(decision);
        return true;
    }

    internal List<QuoteCustomerDecisionRecord> CustomerDecisions { get; private set; } = [];
    internal QuoteSalesAcceptance? SalesAcceptance { get; private set; }
    internal bool AcceptSales(QuoteSalesAcceptance acceptance, DateTimeOffset now)
    {
        if(Status!="SENT" || ArchivedAt is not null || SalesAcceptance is not null
            || acceptance.WorkspaceId!=WorkspaceId || acceptance.QuoteId!=QuoteId)return false;
        SalesAcceptance=acceptance; Status="ACCEPTED"; AcceptedAt=now; UpdatedAt=now; ResourceVersion++;
        return true;
    }

    internal string WorkspaceId { get; private set; } = null!;
    internal string QuoteId { get; private set; } = null!;
    internal string QuoteNumber { get; private set; } = null!;
    internal int QuoteRevision { get; private set; }
    internal string RootQuoteId { get; private set; } = null!;
    internal string? RevisionOfQuoteId { get; private set; }
    internal string BuyerType { get; private set; } = null!;
    internal string BuyerId { get; private set; } = null!;
    internal string SourcePath { get; private set; } = null!;
    internal string? SourceDealId { get; private set; }
    internal string? ContactId { get; private set; }
    internal string? SourceLeadId { get; private set; }
    internal string Status { get; private set; } = null!;
    internal string Title { get; private set; } = null!;
    internal string Currency { get; private set; } = null!;
    internal string? OwnerId { get; private set; }
    internal string? RecipientEmail { get; private set; }
    internal string LineItemsJson { get; private set; } = null!;
    internal string? AdjustmentsJson { get; private set; }
    internal decimal SubtotalAmount { get; private set; }
    internal string SubtotalCurrency { get; private set; } = null!;
    internal decimal DiscountTotalAmount { get; private set; }
    internal string DiscountTotalCurrency { get; private set; } = null!;
    internal decimal TaxTotalAmount { get; private set; }
    internal string TaxTotalCurrency { get; private set; } = null!;
    internal decimal GrandTotalAmount { get; private set; }
    internal string GrandTotalCurrency { get; private set; } = null!;
    internal DateOnly? ValidUntil { get; private set; }
    internal DateTimeOffset? ReviewRequestedAt { get; private set; }
    internal DateTimeOffset? SentAt { get; private set; }
    internal DateTimeOffset? AcceptedAt { get; private set; }
    internal DateTimeOffset? RejectedAt { get; private set; }
    internal DateTimeOffset? ExpiredAt { get; private set; }
    internal string? Notes { get; private set; }
    internal DateTimeOffset? ArchivedAt { get; private set; }
    internal string? ArchiveReason { get; private set; }
    internal string ActionsJson { get; private set; } = null!;
    internal string? ApprovalStatus { get; private set; }
    internal bool? ApprovalRequired { get; private set; }
    internal string? ApprovalReasonsJson { get; private set; }
    internal DateTimeOffset? ApprovalRequestedAt { get; private set; }
    internal string? ApprovalRequestedBy { get; private set; }
    internal DateTimeOffset? ApprovedAt { get; private set; }
    internal string? ApprovedBy { get; private set; }
    internal string? ApprovalDecisionNote { get; private set; }
    internal string? ApprovalContentFingerprint { get; private set; }
    internal string? ApprovalPolicyVersion { get; private set; }
    internal string? PaymentAgreementJson { get; private set; }
    internal string? ProductSnapshotsJson { get; private set; }
    internal string? DeliveryHistoryJson { get; private set; }
    internal string? SenderName { get; private set; }
    internal string? SenderAddress { get; private set; }
    internal string? SenderEmail { get; private set; }
    internal string? SenderTaxId { get; private set; }
    internal long ResourceVersion { get; private set; }
    internal DateTimeOffset CreatedAt { get; private set; }
    internal DateTimeOffset UpdatedAt { get; private set; }
}

internal sealed record QuoteDraftContent(string Title, string Currency, string OwnerId, string? RecipientEmail,
    string LineItemsJson, string AdjustmentsJson, string ProductSnapshotsJson, decimal Subtotal, decimal Discount,
    decimal Tax, decimal Total, DateOnly? ValidUntil, string? Notes, string? PaymentAgreementJson);
