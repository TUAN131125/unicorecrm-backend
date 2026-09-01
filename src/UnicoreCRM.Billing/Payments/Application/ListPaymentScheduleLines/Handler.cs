using System.Text.RegularExpressions;
using UnicoreCRM.Billing.Payments.Application.Common;
using UnicoreCRM.Billing.Payments.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Billing.Payments.Application.ListPaymentScheduleLines;

internal sealed record Query(string? PlanId, PaymentRequestMetadata Metadata);

internal sealed partial class Handler(PaymentAuthorization authorization, IPaymentsPersistence persistence, TimeProvider timeProvider)
{
    internal async Task<PaymentOperationResult<IReadOnlyList<PaymentScheduleLineDocument>>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizePaymentScheduleLinesAsync(query.Metadata, cancellationToken);
        if (!access.IsSuccess)
            return PaymentOperationResult<IReadOnlyList<PaymentScheduleLineDocument>>.Failure(access.Error!);

        if (query.PlanId is not null && !EntityIdPattern().IsMatch(query.PlanId))
        {
            return PaymentOperationResult<IReadOnlyList<PaymentScheduleLineDocument>>.Failure(
                PaymentErrors.Validation(new Dictionary<string, string[]> { ["planId"] = ["planId must be a valid EntityId."] }));
        }

        PaymentScheduleLineDocument[] documents = [];
        if (access.Value!.Authorization.ScopeFilter == RecordAccessScopeFilter.Workspace)
        {
            var items = await persistence.ReadPaymentScheduleLinesAsync(
                access.Value.Trusted.WorkspaceId,
                query.PlanId,
                cancellationToken);
            documents = items
                .Select(item => PaymentFieldSecurity.Project(PaymentProjection.ScheduleLine(item), access.Value.Authorization))
                .ToArray();
        }

        // One row per successful invocation, never one per returned entity, and written only after
        // every projection has succeeded. An empty successful result still writes it.
        await PaymentReadAudit.RecordListAsync(
            persistence, access.Value, query.Metadata, "listPaymentScheduleLines", timeProvider.GetUtcNow(), cancellationToken);
        return PaymentOperationResult<IReadOnlyList<PaymentScheduleLineDocument>>.Success(documents);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
