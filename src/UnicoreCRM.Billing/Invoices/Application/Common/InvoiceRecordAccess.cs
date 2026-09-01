using UnicoreCRM.Billing.Invoices.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Billing.Invoices.Application.Common;

internal sealed record InvoiceAccess(TrustedWorkspaceContext Trusted, RecordAccessAuthorization Authorization);

/// <summary>
/// The Invoices field-security vocabulary.
///
/// <para><c>getInvoice</c> and <c>listInvoices</c> both return the adopted <c>InvoiceDocument</c>,
/// so there is exactly one representation. The boolean is the contract required-ness of the field
/// in that representation: a restrictive policy on a required field has no admitted absent
/// representation and must fail the operation closed, while an optional field is simply omitted.
/// </para>
/// </summary>
internal static class InvoiceFieldSecurity
{
    private static IReadOnlyDictionary<string, bool> InvoiceFields { get; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = true,
            ["buyerRef"] = true,
            ["sellerSnapshot"] = true,
            ["buyerSnapshot"] = true,
            ["lifecycleState"] = true,
            ["deliveryState"] = true,
            ["currency"] = true,
            ["lines"] = true,
            ["totals"] = true,
            ["sourceLinks"] = true,
            ["version"] = true,
            ["idempotencyKey"] = true,
            ["createdAt"] = true,
            ["updatedAt"] = true,
            ["workspaceId"] = false,
            ["invoiceNumber"] = false,
            ["issueDate"] = false,
            ["dueDate"] = false,
            ["exchangeRateSnapshot"] = false,
            ["paymentTerms"] = false,
            ["creationIntentId"] = false,
            ["issuedAt"] = false,
            ["issueFailureCode"] = false,
            ["issueEvidence"] = false,
            ["discardedAt"] = false,
            ["voidedAt"] = false,
            ["voidReason"] = false
        };

    internal static IReadOnlyDictionary<string, bool> EnforceableFields => InvoiceFields;

    internal static IReadOnlyList<string> InvoiceFieldKeys { get; } =
        InvoiceFields.Keys.Order(StringComparer.Ordinal).ToArray();

    internal static RecordAccessRepresentation InvoiceRepresentation { get; } =
        RecordAccessRepresentation.Create(
            "invoices.invoice.full",
            InvoiceFields.Where(field => !field.Value).Select(field => field.Key).ToArray());

    internal static InvoiceOperationError? UnenforceablePolicy(RecordAccessAuthorization access) =>
        access.UnenforceableFieldKeys.Count == 0 ? null : InvoiceErrors.AccessDenied();

    internal static InvoiceDocument Project(InvoiceDocument model, RecordAccessAuthorization access) => model with
    {
        WorkspaceId = Keep(access, "workspaceId", model.WorkspaceId),
        InvoiceNumber = Keep(access, "invoiceNumber", model.InvoiceNumber),
        IssueDate = Keep(access, "issueDate", model.IssueDate),
        DueDate = Keep(access, "dueDate", model.DueDate),
        ExchangeRateSnapshot = Keep(access, "exchangeRateSnapshot", model.ExchangeRateSnapshot),
        PaymentTerms = Keep(access, "paymentTerms", model.PaymentTerms),
        CreationIntentId = Keep(access, "creationIntentId", model.CreationIntentId),
        IssuedAt = Keep(access, "issuedAt", model.IssuedAt),
        IssueFailureCode = Keep(access, "issueFailureCode", model.IssueFailureCode),
        IssueEvidence = Keep(access, "issueEvidence", model.IssueEvidence),
        DiscardedAt = Keep(access, "discardedAt", model.DiscardedAt),
        VoidedAt = Keep(access, "voidedAt", model.VoidedAt),
        VoidReason = Keep(access, "voidReason", model.VoidReason)
    };

    private static T? Keep<T>(RecordAccessAuthorization access, string field, T? value) =>
        access.CanRead(field) ? value : default;
}

internal sealed class InvoiceAuthorization(IRecordAccessEvaluator evaluator)
{
    internal const string ResourceKey = "invoices";

    internal async Task<InvoiceOperationResult<InvoiceAccess>> AuthorizeInvoicesAsync(
        InvoiceRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var authorization = await evaluator.AuthorizeResourceAsync(
            ResourceKey,
            InvoiceCapabilities.Read.Capability,
            InvoiceFieldSecurity.InvoiceFieldKeys,
            InvoiceFieldSecurity.InvoiceRepresentation,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);

        if (authorization.TrustedWorkspace is not { } trusted)
        {
            return InvoiceOperationResult<InvoiceAccess>.Failure(
                authorization.Code == "WORKSPACE_MISMATCH"
                    ? InvoiceErrors.WorkspaceMismatch()
                    : InvoiceErrors.AccessDenied());
        }
        if (!authorization.IsAllowed)
            return InvoiceOperationResult<InvoiceAccess>.Failure(InvoiceErrors.AccessDenied());

        var fieldError = InvoiceFieldSecurity.UnenforceablePolicy(authorization);
        return fieldError is null
            ? InvoiceOperationResult<InvoiceAccess>.Success(new InvoiceAccess(trusted, authorization))
            : InvoiceOperationResult<InvoiceAccess>.Failure(fieldError);
    }

    /// <summary>
    /// Record-scope enforcement for a single Invoice the owner has already loaded from its own
    /// Workspace-scoped persistence. No authoritative Invoice ownership or team fact exists in
    /// current authority, so the owner member is null and OWN, TEAM and CUSTOM all deny.
    /// </summary>
    internal async Task<InvoiceOperationError?> EnforceRecordAsync(
        InvoiceAccess access,
        string recordId,
        string enforcementPoint,
        InvoiceRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var decision = await evaluator.AuthorizeRecordAsync(
            access.Authorization,
            recordId,
            RecordAccessFacts.Found(null),
            enforcementPoint,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);
        return decision.IsAllowed ? null : InvoiceErrors.NotFound();
    }
}
