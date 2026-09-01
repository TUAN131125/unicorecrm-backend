using UnicoreCRM.Billing.Invoices.Domain;

namespace UnicoreCRM.Billing.Invoices.Application.Common;

internal sealed record InvoiceRequestMetadata(string RequestId, string CorrelationId);

internal sealed record InvoiceOperationError(
    string Code,
    int Status,
    string Title,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null);

internal sealed record InvoiceOperationResult<T>(T? Value, InvoiceOperationError? Error)
{
    internal bool IsSuccess => Error is null;
    internal static InvoiceOperationResult<T> Success(T value) => new(value, null);
    internal static InvoiceOperationResult<T> Failure(InvoiceOperationError error) => new(default, error);
}

/// <summary>
/// The Invoices-owned persistence boundary. Every member is Workspace-scoped by its first
/// parameter; no operation reaches a foreign owner's context, table or runtime.
/// </summary>
internal interface IInvoicesPersistence
{
    Task<IReadOnlyList<Invoice>> ReadInvoicesAsync(string workspaceId, CancellationToken cancellationToken);

    Task<Invoice?> ReadInvoiceAsync(string workspaceId, string invoiceId, CancellationToken cancellationToken);

    Task<bool> RecordExistsAsync(string workspaceId, string recordId, CancellationToken cancellationToken);

    /// <summary>Stages the Invoices-owned read-evidence row for a successful disclosure.</summary>
    void AddReadAudit(InvoiceReadAuditRecord readAudit);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal static class InvoiceErrors
{
    internal static InvoiceOperationError AccessDenied() => new("ACCESS_DENIED", 403, "Access denied");
    internal static InvoiceOperationError WorkspaceMismatch() => new("WORKSPACE_MISMATCH", 403, "Workspace context mismatch");
    internal static InvoiceOperationError NotFound() => new("RESOURCE_NOT_FOUND", 404, "Resource not found");
    internal static InvoiceOperationError Validation(IReadOnlyDictionary<string, string[]> fields, int status = 422) =>
        new("VALIDATION_FAILED", status, "Validation failed", fields);
}
