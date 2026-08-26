using System.Globalization;
using UnicoreCRM.Operations.Support.Contracts;
using UnicoreCRM.Operations.Support.Domain;

namespace UnicoreCRM.Operations.Support.Application.Common;

/// <summary>
/// Maps Support-owned state onto the exact declared wire vocabulary. EF entities never leave
/// the owner; Support-owned allocation state such as the case sequence is never projected.
/// </summary>
internal static class SupportProjection
{
    /// <summary>
    /// The fail-closed SLA projection.
    ///
    /// <para>Support authority proves that SLA deadline fields exist and that the
    /// <c>SupportCaseSlaStatus</c> vocabulary contains <c>on_track</c>, <c>at_risk</c>,
    /// <c>breached</c>, <c>paused</c> and <c>not_applicable</c>. It does not define the
    /// deadline policy, the at-risk threshold, the pause conditions, or the event that
    /// satisfies a first response. SLA configuration administration is also outside the
    /// admitted scope. Every value except <c>not_applicable</c> would therefore be an
    /// invented compliance claim, so Support reports <c>not_applicable</c> until an SLA
    /// authority is admitted. The stored due timestamps remain exactly what the caller
    /// declared.</para>
    /// </summary>
    internal const string UnresolvedSlaStatus = "not_applicable";

    internal static SupportCaseReadModel Case(SupportCase item) => new(
        item.CaseId,
        item.CaseNumber,
        item.Title,
        item.Description,
        Status(item.Status),
        Priority(item.Priority),
        Category(item.Category),
        Source(item.Source),
        new SupportBuyerRef(item.RelationshipType, item.RelationshipId),
        Utc(item.CreatedAt),
        Utc(item.UpdatedAt),
        UnresolvedSlaStatus,
        item.Version,
        item.Channel is null ? null : Channel(item.Channel.Value),
        item.ContactId,
        item.RelatedOrderId,
        item.RelatedProductId,
        item.RelatedOwnedProductId,
        item.OwnerId,
        OptionalUtc(item.FirstResponseDueAt),
        OptionalUtc(item.ResolutionDueAt),
        OptionalUtc(item.ResolvedAt),
        OptionalUtc(item.ClosedAt),
        OptionalUtc(item.NextFollowUpAt),
        OptionalUtc(item.ReopenedAt),
        item.Tags.Count == 0 ? null : item.Tags,
        item.ResolutionSummary);

    internal static string Status(SupportCaseStatus value) => value switch
    {
        SupportCaseStatus.New => "new",
        SupportCaseStatus.InProgress => "in_progress",
        SupportCaseStatus.WaitingCustomer => "waiting_customer",
        SupportCaseStatus.WaitingInternal => "waiting_internal",
        SupportCaseStatus.Resolved => "resolved",
        SupportCaseStatus.Closed => "closed",
        SupportCaseStatus.Reopened => "reopened",
        SupportCaseStatus.Cancelled => "cancelled",
        _ => throw new InvalidOperationException("Unknown Support Case status.")
    };

    internal static string Priority(SupportCasePriority value) => value switch
    {
        SupportCasePriority.Low => "low",
        SupportCasePriority.Medium => "medium",
        SupportCasePriority.High => "high",
        SupportCasePriority.Critical => "critical",
        _ => throw new InvalidOperationException("Unknown Support Case priority.")
    };

    internal static string Category(SupportCaseCategory value) => value switch
    {
        SupportCaseCategory.Request => "request",
        SupportCaseCategory.Consultation => "consultation",
        SupportCaseCategory.Complaint => "complaint",
        SupportCaseCategory.FollowUp => "follow_up",
        SupportCaseCategory.Onboarding => "onboarding",
        SupportCaseCategory.UsageIssue => "usage_issue",
        SupportCaseCategory.PostPurchase => "post_purchase",
        SupportCaseCategory.TechnicalSupport => "technical_support",
        SupportCaseCategory.Warranty => "warranty",
        SupportCaseCategory.CustomerCare => "customer_care",
        SupportCaseCategory.Billing => "billing",
        SupportCaseCategory.FeatureRequest => "feature_request",
        _ => throw new InvalidOperationException("Unknown Support Case category.")
    };

    internal static string Source(SupportCaseSource value) => value switch
    {
        SupportCaseSource.Manual => "manual",
        SupportCaseSource.Customer360 => "customer_360",
        SupportCaseSource.Email => "email",
        SupportCaseSource.Phone => "phone",
        SupportCaseSource.Chat => "chat",
        SupportCaseSource.WebForm => "web_form",
        SupportCaseSource.Order => "order",
        SupportCaseSource.Product => "product",
        _ => throw new InvalidOperationException("Unknown Support Case source.")
    };

    internal static string Channel(SupportCaseChannel value) => value switch
    {
        SupportCaseChannel.Email => "email",
        SupportCaseChannel.Phone => "phone",
        SupportCaseChannel.Chat => "chat",
        SupportCaseChannel.Meeting => "meeting",
        SupportCaseChannel.Internal => "internal",
        _ => throw new InvalidOperationException("Unknown Support Case channel.")
    };

    internal static string Utc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static string? OptionalUtc(DateTimeOffset? value) => value is null ? null : Utc(value.Value);
}
