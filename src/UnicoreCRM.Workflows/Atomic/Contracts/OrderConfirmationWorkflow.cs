using System.Text.Json.Serialization;
namespace UnicoreCRM.Workflows.Atomic.Contracts;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfirmOrderWithPaymentPlanRequest([property:JsonRequired] string PaymentPlanId,[property:JsonRequired] long ExpectedPaymentPlanVersion,
    bool OperationalReviewConfirmed=false,string? OperationalReviewNotes=null,string? PaymentAccountId=null,string? CreditApprovalId=null,
    UnicoreCRM.Sales.Orders.Contracts.OrderConfirmationFulfillment? Fulfillment=null);
public sealed record ConfirmedOrderTransactionResult(string OrderId,string OrderState,string ConfirmedAt,string PaymentPlanId,
    string PaymentPlanState,long PaymentPlanVersion,bool PaymentInstructionCreated);
public sealed record ConfirmOrderWithPaymentPlanResponse(string CommandId,string CorrelationId,string AggregateId,string AggregateType,
    long Version,string OccurredAt,string Outcome,ConfirmedOrderTransactionResult Result,IReadOnlyList<string> Warnings,
    IReadOnlyList<string> EmittedEventIds,IReadOnlyList<string> AuditEvidenceIds);
