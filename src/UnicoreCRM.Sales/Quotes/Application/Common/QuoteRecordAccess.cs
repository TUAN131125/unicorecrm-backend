using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;

namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed record QuoteAccess(TrustedWorkspaceContext Trusted, RecordAccessAuthorization Authorization);

internal static class QuoteFieldSecurity
{
    internal static IReadOnlyDictionary<string, bool> EnforceableFields { get; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = true,
            ["quoteNumber"] = true,
            ["quoteRevision"] = true,
            ["rootQuoteId"] = true,
            ["revisionOfQuoteId"] = false,
            ["buyerRef"] = true,
            ["sourcePath"] = true,
            ["sourceDealId"] = false,
            ["contactId"] = false,
            ["sourceLeadId"] = false,
            ["status"] = true,
            ["title"] = true,
            ["currency"] = true,
            ["ownerId"] = false,
            ["recipientEmail"] = false,
            ["lineItems"] = true,
            ["adjustments"] = false,
            ["subtotal"] = true,
            ["discountTotal"] = true,
            ["taxTotal"] = true,
            ["grandTotal"] = true,
            ["validUntil"] = false,
            ["reviewRequestedAt"] = false,
            ["sentAt"] = false,
            ["acceptedAt"] = false,
            ["rejectedAt"] = false,
            ["expiredAt"] = false,
            ["notes"] = false,
            ["archivedAt"] = false,
            ["archiveReason"] = false,
            ["actions"] = true,
            ["resourceVersion"] = true,
            ["createdAt"] = true,
            ["updatedAt"] = true,
            ["approvalStatus"] = false,
            ["approvalRequired"] = false,
            ["approvalReasons"] = false,
            ["approvalRequestedAt"] = false,
            ["approvalRequestedBy"] = false,
            ["approvedAt"] = false,
            ["approvedBy"] = false,
            ["approvalDecisionNote"] = false,
            ["approvalContentFingerprint"] = false,
            ["approvalPolicyVersion"] = false,
            ["paymentAgreement"] = false,
            ["deliveryHistory"] = false,
            ["senderName"] = false,
            ["senderAddress"] = false,
            ["senderEmail"] = false,
            ["senderTaxId"] = false
        };

    internal static IReadOnlyList<string> FieldKeys { get; } =
        EnforceableFields.Keys.Order(StringComparer.Ordinal).ToArray();

    internal static QuoteOperationError? UnenforceablePolicy(RecordAccessAuthorization access) =>
        access.UnenforceableFieldKeys.Count == 0
            ? null
            : new QuoteOperationError(
                "ACCESS_DENIED",
                403,
                "Access denied",
                "A field-security policy applies to a required Quote field, so the request is refused rather than returning a value the policy forbids.");

    internal static QuoteReadModel Project(QuoteReadModel model, RecordAccessAuthorization access) =>
        model with
        {
            RevisionOfQuoteId = Keep(access, "revisionOfQuoteId", model.RevisionOfQuoteId),
            SourceDealId = Keep(access, "sourceDealId", model.SourceDealId),
            ContactId = Keep(access, "contactId", model.ContactId),
            SourceLeadId = Keep(access, "sourceLeadId", model.SourceLeadId),
            OwnerId = Keep(access, "ownerId", model.OwnerId),
            RecipientEmail = Keep(access, "recipientEmail", model.RecipientEmail),
            Adjustments = Keep(access, "adjustments", model.Adjustments),
            ValidUntil = Keep(access, "validUntil", model.ValidUntil),
            ReviewRequestedAt = Keep(access, "reviewRequestedAt", model.ReviewRequestedAt),
            SentAt = Keep(access, "sentAt", model.SentAt),
            AcceptedAt = Keep(access, "acceptedAt", model.AcceptedAt),
            RejectedAt = Keep(access, "rejectedAt", model.RejectedAt),
            ExpiredAt = Keep(access, "expiredAt", model.ExpiredAt),
            Notes = Keep(access, "notes", model.Notes),
            ArchivedAt = Keep(access, "archivedAt", model.ArchivedAt),
            ArchiveReason = Keep(access, "archiveReason", model.ArchiveReason),
            ApprovalStatus = Keep(access, "approvalStatus", model.ApprovalStatus),
            ApprovalRequired = Keep(access, "approvalRequired", model.ApprovalRequired),
            ApprovalReasons = Keep(access, "approvalReasons", model.ApprovalReasons),
            ApprovalRequestedAt = Keep(access, "approvalRequestedAt", model.ApprovalRequestedAt),
            ApprovalRequestedBy = Keep(access, "approvalRequestedBy", model.ApprovalRequestedBy),
            ApprovedAt = Keep(access, "approvedAt", model.ApprovedAt),
            ApprovedBy = Keep(access, "approvedBy", model.ApprovedBy),
            ApprovalDecisionNote = Keep(access, "approvalDecisionNote", model.ApprovalDecisionNote),
            ApprovalContentFingerprint = Keep(access, "approvalContentFingerprint", model.ApprovalContentFingerprint),
            ApprovalPolicyVersion = Keep(access, "approvalPolicyVersion", model.ApprovalPolicyVersion),
            PaymentAgreement = Keep(access, "paymentAgreement", model.PaymentAgreement),
            DeliveryHistory = Keep(access, "deliveryHistory", model.DeliveryHistory),
            SenderName = Keep(access, "senderName", model.SenderName),
            SenderAddress = Keep(access, "senderAddress", model.SenderAddress),
            SenderEmail = Keep(access, "senderEmail", model.SenderEmail),
            SenderTaxId = Keep(access, "senderTaxId", model.SenderTaxId)
        };

    private static T? Keep<T>(RecordAccessAuthorization access, string fieldKey, T? value) =>
        access.CanRead(fieldKey) ? value : default;
}

internal sealed class QuoteAuthorization(IRecordAccessEvaluator evaluator)
{
    internal const string ResourceKey = "quotes";

    internal async Task<QuoteOperationResult<QuoteAccess>> AuthorizeAsync(
        QuoteRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var authorization = await evaluator.AuthorizeResourceAsync(
            ResourceKey,
            QuoteCapabilities.Read.Capability,
            QuoteFieldSecurity.FieldKeys,
            RecordAccessRepresentation.Full,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);

        if (authorization.TrustedWorkspace is not { } trusted)
        {
            return QuoteOperationResult<QuoteAccess>.Failure(
                authorization.Code == "WORKSPACE_MISMATCH"
                    ? QuoteErrors.WorkspaceMismatch()
                    : QuoteErrors.AccessDenied());
        }
        if (!authorization.IsAllowed)
            return QuoteOperationResult<QuoteAccess>.Failure(QuoteErrors.AccessDenied());

        var unenforceable = QuoteFieldSecurity.UnenforceablePolicy(authorization);
        return unenforceable is null
            ? QuoteOperationResult<QuoteAccess>.Success(new QuoteAccess(trusted, authorization))
            : QuoteOperationResult<QuoteAccess>.Failure(unenforceable);
    }

    internal async Task<QuoteOperationError?> EnforceRecordAsync(
        QuoteAccess access,
        Quote quote,
        string enforcementPoint,
        QuoteRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var decision = await evaluator.AuthorizeRecordAsync(
            access.Authorization,
            quote.QuoteId,
            Facts(quote),
            enforcementPoint,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);
        return decision.IsAllowed ? null : QuoteErrors.NotFound();
    }

    // ownerId is admitted as a Quote wire field, but current authority does not identify it as the
    // canonical AccessControl owner fact. It therefore cannot widen OWN scope; TEAM and CUSTOM are
    // already fail-closed in AccessControl.
    internal static RecordAccessFacts Facts(Quote quote) => RecordAccessFacts.Found(null);
}
