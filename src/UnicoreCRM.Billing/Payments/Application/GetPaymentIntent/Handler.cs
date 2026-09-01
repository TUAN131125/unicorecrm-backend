using System.Text.RegularExpressions;
using UnicoreCRM.Billing.Payments.Application.Common;
using UnicoreCRM.Billing.Payments.Contracts;

namespace UnicoreCRM.Billing.Payments.Application.GetPaymentIntent;

internal sealed record Query(string PaymentIntentId, PaymentRequestMetadata Metadata);

internal sealed partial class Handler(PaymentAuthorization authorization, IPaymentsPersistence persistence, TimeProvider timeProvider)
{
    internal async Task<PaymentOperationResult<PaymentIntentDocument>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizePaymentIntentsAsync(query.Metadata, cancellationToken);
        if (!access.IsSuccess)
            return PaymentOperationResult<PaymentIntentDocument>.Failure(access.Error!);
        if (!EntityIdPattern().IsMatch(query.PaymentIntentId))
            return PaymentOperationResult<PaymentIntentDocument>.Failure(PaymentErrors.NotFound());

        var intent = await persistence.ReadPaymentIntentAsync(
            access.Value!.Trusted.WorkspaceId,
            query.PaymentIntentId,
            cancellationToken);
        if (intent is null)
            return PaymentOperationResult<PaymentIntentDocument>.Failure(PaymentErrors.NotFound());

        var denied = await authorization.EnforceRecordAsync(
            access.Value,
            intent.PaymentIntentId,
            "getPaymentIntent",
            query.Metadata,
            cancellationToken);
        if (denied is not null)
            return PaymentOperationResult<PaymentIntentDocument>.Failure(denied);

        var document = PaymentFieldSecurity.Project(PaymentProjection.Intent(intent), access.Value.Authorization);
        await PaymentReadAudit.RecordResourceAsync(
            persistence, access.Value, query.Metadata, "getPaymentIntent",
            intent.PaymentIntentId, intent.ResourceVersion, timeProvider.GetUtcNow(), cancellationToken);
        return PaymentOperationResult<PaymentIntentDocument>.Success(document);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
