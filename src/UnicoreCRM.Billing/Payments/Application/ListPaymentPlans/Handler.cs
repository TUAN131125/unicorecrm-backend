using System.Text.RegularExpressions;
using UnicoreCRM.Billing.Payments.Application.Common;
using UnicoreCRM.Billing.Payments.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Billing.Payments.Application.ListPaymentPlans;

internal sealed record Query(string? OrderId, PaymentRequestMetadata Metadata);

internal sealed partial class Handler(PaymentAuthorization authorization, IPaymentsPersistence persistence, TimeProvider timeProvider)
{
    internal async Task<PaymentOperationResult<IReadOnlyList<PaymentPlanDocument>>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizePaymentPlansAsync(query.Metadata, cancellationToken);
        if (!access.IsSuccess)
            return PaymentOperationResult<IReadOnlyList<PaymentPlanDocument>>.Failure(access.Error!);

        if (query.OrderId is not null && !EntityIdPattern().IsMatch(query.OrderId))
        {
            return PaymentOperationResult<IReadOnlyList<PaymentPlanDocument>>.Failure(
                PaymentErrors.Validation(new Dictionary<string, string[]> { ["orderId"] = ["orderId must be a valid EntityId."] }));
        }

        PaymentPlanDocument[] documents = [];
        if (access.Value!.Authorization.ScopeFilter == RecordAccessScopeFilter.Workspace)
        {
            var items = await persistence.ReadPaymentPlansAsync(
                access.Value.Trusted.WorkspaceId,
                query.OrderId,
                cancellationToken);
            documents = items
                .Select(item => PaymentFieldSecurity.Project(PaymentProjection.Plan(item), access.Value.Authorization))
                .ToArray();
        }

        // One row per successful invocation, never one per returned entity, and written only after
        // every projection has succeeded. An empty successful result still writes it.
        await PaymentReadAudit.RecordListAsync(
            persistence, access.Value, query.Metadata, "listPaymentPlans", timeProvider.GetUtcNow(), cancellationToken);
        return PaymentOperationResult<IReadOnlyList<PaymentPlanDocument>>.Success(documents);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
