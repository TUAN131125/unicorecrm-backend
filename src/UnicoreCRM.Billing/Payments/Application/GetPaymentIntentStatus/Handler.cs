using System.Text.RegularExpressions;
using UnicoreCRM.Billing.Payments.Application.Common;
using UnicoreCRM.Billing.Payments.Contracts;

namespace UnicoreCRM.Billing.Payments.Application.GetPaymentIntentStatus;

internal sealed record Query(string PaymentIntentId, PaymentRequestMetadata Metadata);

internal sealed partial class Handler(PaymentAuthorization authorization, IPaymentsPersistence persistence, TimeProvider timeProvider)
{
    internal async Task<PaymentOperationResult<PaymentIntentStatusResponse>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizePaymentIntentStatusAsync(query.Metadata, cancellationToken);
        if (!access.IsSuccess)
            return PaymentOperationResult<PaymentIntentStatusResponse>.Failure(access.Error!);
        if (!EntityIdPattern().IsMatch(query.PaymentIntentId))
            return PaymentOperationResult<PaymentIntentStatusResponse>.Failure(PaymentErrors.NotFound());

        var intent = await persistence.ReadPaymentIntentAsync(
            access.Value!.Trusted.WorkspaceId,
            query.PaymentIntentId,
            cancellationToken);
        if (intent is null)
            return PaymentOperationResult<PaymentIntentStatusResponse>.Failure(PaymentErrors.NotFound());

        var denied = await authorization.EnforceRecordAsync(
            access.Value,
            intent.PaymentIntentId,
            "getPaymentIntentStatus",
            query.Metadata,
            cancellationToken);
        if (denied is not null)
            return PaymentOperationResult<PaymentIntentStatusResponse>.Failure(denied);

        var document = PaymentFieldSecurity.Project(PaymentProjection.IntentStatus(intent), access.Value.Authorization);
        await PaymentReadAudit.RecordResourceAsync(
            persistence, access.Value, query.Metadata, "getPaymentIntentStatus",
            intent.PaymentIntentId, intent.ResourceVersion, timeProvider.GetUtcNow(), cancellationToken);
        return PaymentOperationResult<PaymentIntentStatusResponse>.Success(document);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
