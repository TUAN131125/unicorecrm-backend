namespace UnicoreCRM.Sales.Orders.Domain;

/// <summary>
/// Orders-owned state created from an immutable accepted commercial snapshot.
/// </summary>
internal sealed class Order
{
    internal void Confirm(string actor,string reviewNotes,UnicoreCRM.Sales.Orders.Contracts.OrderConfirmationFulfillment? fulfillment,DateTimeOffset now,string origin="STAFF",string? policyVersion=null)
    {
        if(origin=="WORKSPACE_POLICY" && (actor!="system:quote-policy" || string.IsNullOrWhiteSpace(policyVersion)))throw new InvalidOperationException("Automatic confirmation requires system policy origin.");
        if(State!="DRAFT")throw new InvalidOperationException("Order is not a draft.");
        if(fulfillment is not null){RecipientName=fulfillment.RecipientName.Trim();RecipientPhone=fulfillment.RecipientPhone.Trim();ShippingAddressJson=System.Text.Json.JsonSerializer.Serialize(fulfillment.ShippingAddress,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));}
        State="CONFIRMED";ConfirmedAt=now;ConfirmedBy=actor;ConfirmationOrigin=origin;ConfirmationPolicyVersion=policyVersion;OperationalReviewNotes=reviewNotes;
        ResourceVersion++;UpdatedAt=now;
        ActionsJson="{\"confirm\":{\"allowed\":false,\"blockerCodes\":[\"ORDER_CONFIRMATION_BLOCKED\"]},\"cancel\":{\"allowed\":false,\"blockerCodes\":[\"CONTRACT_OPERATION_BLOCKED\"]}}";
    }
    internal string? ConfirmationOrigin { get; private set; }
    internal string? ConfirmationPolicyVersion { get; private set; }
    internal string? ConfirmedBy { get; private set; }
    internal string? OperationalReviewNotes { get; private set; }
    private Order() { }
    internal static Order FromAccepted(Contracts.AcceptedQuoteOrderDraft source, OrderAcceptedSnapshot q, DateTimeOffset now)
    {
        AcceptedOrderValidation.Validate(q);
        var json=new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var acceptance=System.Text.Json.JsonDocument.Parse(source.AcceptanceJson).RootElement;
        return new Order
        {
            WorkspaceId=source.WorkspaceId,OrderId="order_"+Guid.NewGuid().ToString("N"),OrderNumber="ORD-"+Guid.NewGuid().ToString("N").ToUpperInvariant(),
            OrderDate=DateOnly.FromDateTime(now.UtcDateTime),BuyerType=q.BuyerRef.Type,BuyerId=q.BuyerRef.Id,ContactId=q.ContactId,
            SourceLeadId=q.SourceLeadId,SourceQuoteId=source.QuoteId,SourceQuoteNumber=q.QuoteNumber,SourceDealId=q.SourceDealId,
            State="DRAFT",LineItemsJson=System.Text.Json.JsonSerializer.Serialize(q.LineItems,json),
            AdjustmentsJson=q.Adjustments is null?null:System.Text.Json.JsonSerializer.Serialize(q.Adjustments,json),
            SubtotalAmount=AcceptedOrderValidation.Amount(q.Subtotal,q.Currency),SubtotalCurrency=q.Currency,
            DiscountTotalAmount=AcceptedOrderValidation.Amount(q.DiscountTotal,q.Currency),DiscountTotalCurrency=q.Currency,
            TaxTotalAmount=AcceptedOrderValidation.Amount(q.TaxTotal,q.Currency),TaxTotalCurrency=q.Currency,
            GrandTotalAmount=AcceptedOrderValidation.Amount(q.GrandTotal,q.Currency),GrandTotalCurrency=q.Currency,Currency=q.Currency,
            OwnerId=q.OwnerId,RecipientEmail=q.RecipientEmail,BuyerNameSnapshot=q.BuyerNameSnapshot,Notes=q.Notes,ResourceVersion=1,CreatedAt=now,UpdatedAt=now,
            ActionsJson="{\"confirm\":{\"allowed\":false,\"blockerCodes\":[\"ORDER_CONFIRMATION_BLOCKED\"]},\"cancel\":{\"allowed\":false,\"blockerCodes\":[\"CONTRACT_OPERATION_BLOCKED\"]}}",
            PaymentAgreementSnapshotJson=q.PaymentAgreement.GetRawText(),AcceptanceJson=source.AcceptanceJson,
            SourceAcceptanceId=source.AcceptanceId,SourceQuoteResourceVersion=q.ResourceVersion,CommercialSnapshotFingerprint=source.CommercialFingerprint,
            OperationalApprovalRequired=acceptance.GetProperty("operationalApprovalRequired").GetBoolean()
        };
    }

    internal string WorkspaceId { get; private set; } = null!;
    internal string OrderId { get; private set; } = null!;
    internal string OrderNumber { get; private set; } = null!;
    internal DateOnly OrderDate { get; private set; }
    internal string BuyerType { get; private set; } = null!;
    internal string BuyerId { get; private set; } = null!;
    internal string? ContactId { get; private set; }
    internal string? SourceLeadId { get; private set; }
    internal string? SourceQuoteId { get; private set; }
    internal string? SourceQuoteNumber { get; private set; }
    internal string? SourceDealId { get; private set; }
    internal string State { get; private set; } = null!;
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
    internal string Currency { get; private set; } = null!;
    internal DateTimeOffset? ConfirmedAt { get; private set; }
    internal DateTimeOffset? CompletedAt { get; private set; }
    internal DateTimeOffset? CancelledAt { get; private set; }
    internal DateOnly? ExpectedDeliveryDate { get; private set; }
    internal string? RecipientName { get; private set; }
    internal string? RecipientPhone { get; private set; }
    internal string? RecipientEmail { get; private set; }
    internal string? ShippingAddressJson { get; private set; }
    internal string? OwnerId { get; private set; }
    internal string? Notes { get; private set; }
    internal string? CreditPolicyEvaluationJson { get; private set; }
    internal string ActionsJson { get; private set; } = null!;
    internal DateTimeOffset? ArchivedAt { get; private set; }
    internal string? ArchiveReason { get; private set; }
    internal long ResourceVersion { get; private set; }
    internal DateTimeOffset CreatedAt { get; private set; }
    internal DateTimeOffset UpdatedAt { get; private set; }
    internal string? CreditApprovalJson { get; private set; }
    internal string? PaymentAgreementSnapshotJson { get; private set; }
    internal string? AcceptanceJson { get; private set; }
    internal string? SourceAcceptanceId { get; private set; }
    internal long? SourceQuoteResourceVersion { get; private set; }
    internal string? CommercialSnapshotFingerprint { get; private set; }
    internal bool OperationalApprovalRequired { get; private set; }
    internal string? BuyerNameSnapshot { get; private set; }
}
