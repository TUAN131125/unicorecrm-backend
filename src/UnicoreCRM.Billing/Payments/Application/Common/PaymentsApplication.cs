using UnicoreCRM.Billing.Payments.Domain;

namespace UnicoreCRM.Billing.Payments.Application.Common;

internal sealed record PaymentRequestMetadata(string RequestId, string CorrelationId);

internal sealed record PaymentOperationError(
    string Code,
    int Status,
    string Title,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null);

internal sealed record PaymentOperationResult<T>(T? Value, PaymentOperationError? Error)
{
    internal bool IsSuccess => Error is null;
    internal static PaymentOperationResult<T> Success(T value) => new(value, null);
    internal static PaymentOperationResult<T> Failure(PaymentOperationError error) => new(default, error);
}

internal interface IPaymentsPersistence
{
    Task<IReadOnlyList<PaymentPlan>> ReadPaymentPlansAsync(
        string workspaceId,
        string? orderId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PaymentScheduleLine>> ReadPaymentScheduleLinesAsync(
        string workspaceId,
        string? planId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PaymentIntent>> ReadPaymentIntentsAsync(
        string workspaceId,
        string? orderId,
        CancellationToken cancellationToken);

    Task<PaymentIntent?> ReadPaymentIntentAsync(
        string workspaceId,
        string paymentIntentId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PaymentRecord>> ReadPaymentRecordsAsync(
        string workspaceId,
        string? buyerId,
        CancellationToken cancellationToken);

    Task<PaymentRecord?> ReadPaymentRecordAsync(
        string workspaceId,
        string paymentRecordId,
        CancellationToken cancellationToken);

    Task<bool> RecordExistsAsync(string workspaceId, string recordId, CancellationToken cancellationToken);

    /// <summary>Stages the Payments-owned read-evidence row for a successful disclosure.</summary>
    void AddReadAudit(PaymentReadAuditRecord readAudit);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal static class PaymentErrors
{
    internal static PaymentOperationError AccessDenied() => new("ACCESS_DENIED", 403, "Access denied");
    internal static PaymentOperationError WorkspaceMismatch() => new("WORKSPACE_MISMATCH", 403, "Workspace context mismatch");
    internal static PaymentOperationError NotFound() => new("RESOURCE_NOT_FOUND", 404, "Resource not found");
    internal static PaymentOperationError Validation(IReadOnlyDictionary<string, string[]> fields, int status = 422) =>
        new("VALIDATION_FAILED", status, "Validation failed", fields);
}
