using System.Text.RegularExpressions;
using UnicoreCRM.Billing.Payments.Application.Common;
using UnicoreCRM.Billing.Payments.Contracts;

namespace UnicoreCRM.Billing.Payments.Application.GetPaymentRecordDetail;

internal sealed record Query(string PaymentRecordId, PaymentRequestMetadata Metadata);

internal sealed partial class Handler(PaymentAuthorization authorization, IPaymentsPersistence persistence, TimeProvider timeProvider)
{
    internal async Task<PaymentOperationResult<PaymentRecordDetailResponse>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizePaymentRecordDetailAsync(query.Metadata, cancellationToken);
        if (!access.IsSuccess)
            return PaymentOperationResult<PaymentRecordDetailResponse>.Failure(access.Error!);
        if (!EntityIdPattern().IsMatch(query.PaymentRecordId))
            return PaymentOperationResult<PaymentRecordDetailResponse>.Failure(PaymentErrors.NotFound());

        var record = await persistence.ReadPaymentRecordAsync(
            access.Value!.Trusted.WorkspaceId,
            query.PaymentRecordId,
            cancellationToken);
        if (record is null)
            return PaymentOperationResult<PaymentRecordDetailResponse>.Failure(PaymentErrors.NotFound());

        var denied = await authorization.EnforceRecordAsync(
            access.Value,
            record.PaymentRecordId,
            "getPaymentRecordDetail",
            query.Metadata,
            cancellationToken);
        if (denied is not null)
            return PaymentOperationResult<PaymentRecordDetailResponse>.Failure(denied);

        var document = PaymentFieldSecurity.Project(PaymentProjection.RecordDetail(record), access.Value.Authorization);
        await PaymentReadAudit.RecordResourceAsync(
            persistence, access.Value, query.Metadata, "getPaymentRecordDetail",
            record.PaymentRecordId, record.ResourceVersion, timeProvider.GetUtcNow(), cancellationToken);
        return PaymentOperationResult<PaymentRecordDetailResponse>.Success(document);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
