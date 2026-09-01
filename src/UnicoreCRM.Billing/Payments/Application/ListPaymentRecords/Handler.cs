using System.Text.RegularExpressions;
using UnicoreCRM.Billing.Payments.Application.Common;
using UnicoreCRM.Billing.Payments.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Billing.Payments.Application.ListPaymentRecords;

internal sealed record Query(string? BuyerId, PaymentRequestMetadata Metadata);

internal sealed partial class Handler(PaymentAuthorization authorization, IPaymentsPersistence persistence, TimeProvider timeProvider)
{
    internal async Task<PaymentOperationResult<IReadOnlyList<PaymentRecordDocument>>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizePaymentRecordsAsync(query.Metadata, cancellationToken);
        if (!access.IsSuccess)
            return PaymentOperationResult<IReadOnlyList<PaymentRecordDocument>>.Failure(access.Error!);

        if (query.BuyerId is not null && !EntityIdPattern().IsMatch(query.BuyerId))
        {
            return PaymentOperationResult<IReadOnlyList<PaymentRecordDocument>>.Failure(
                PaymentErrors.Validation(new Dictionary<string, string[]> { ["buyerId"] = ["buyerId must be a valid EntityId."] }));
        }

        PaymentRecordDocument[] documents = [];
        if (access.Value!.Authorization.ScopeFilter == RecordAccessScopeFilter.Workspace)
        {
            var records = await persistence.ReadPaymentRecordsAsync(
                access.Value.Trusted.WorkspaceId,
                query.BuyerId,
                cancellationToken);
            documents = records
                .Select(record => PaymentFieldSecurity.Project(PaymentProjection.Record(record), access.Value.Authorization))
                .ToArray();
        }

        // One row per successful invocation, never one per returned entity, and written only after
        // every projection has succeeded. An empty successful result still writes it.
        await PaymentReadAudit.RecordListAsync(
            persistence, access.Value, query.Metadata, "listPaymentRecords", timeProvider.GetUtcNow(), cancellationToken);
        return PaymentOperationResult<IReadOnlyList<PaymentRecordDocument>>.Success(documents);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
