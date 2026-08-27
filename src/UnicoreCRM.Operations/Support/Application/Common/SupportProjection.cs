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
    /// The SLA projection, held open as a recorded <c>SUPPORT SLA AUTHORITY_GAP</c>.
    ///
    /// <para>This value is a reconciled fail-closed decision, not a convenience default. Every
    /// element the projection would need was searched for across current implementation
    /// authority, the verified OpenAPI, the operation/command/query registries, the Design
    /// Authority and read-only frontend evidence, and none is provable:</para>
    ///
    /// <list type="bullet">
    /// <item><b>Deadline rules.</b> The canonical Support module doc names the
    /// <c>SUPPORT_CASE_SLA_RULES</c> and <c>calculateSupportCaseSla</c> symbols but states no
    /// durations. The only concrete durations live in frontend source, which the frontend
    /// read-only evidence rule forbids from creating backend authority. That frontend
    /// calculator is additionally dead code: no caller invokes it, and the frontend create
    /// command passes the caller-supplied due timestamps straight through instead.</item>
    /// <item><b>First-response semantics.</b> The read model declares
    /// <c>firstRespondedAt</c> and the activity vocabulary declares <c>first_response</c>, but
    /// no authority names the event that satisfies a first response. Support therefore never
    /// sets <c>firstRespondedAt</c>.</item>
    /// <item><b>Breach rule.</b> Only frontend source compares now against the due timestamp,
    /// and its choice to prefer the resolution deadline over the first-response deadline is
    /// stated nowhere in authority.</item>
    /// <item><b>At-risk rule.</b> The only evidence is a frontend heuristic - the greater of
    /// one hour or twenty percent of the resolution limit - self-described in its own comment
    /// as approximate.</item>
    /// <item><b>Pause rule.</b> <c>paused</c> appears in the declared enum and in no
    /// behavioral evidence anywhere; nothing in the repository can produce it.</item>
    /// <item><b>Terminal behavior.</b> Frontend source maps resolved/closed/cancelled to
    /// <c>not_applicable</c> but leaves <c>reopened</c> evaluated. No authority states whether
    /// a terminal or reopened case suspends its SLA clock.</item>
    /// <item><b>Meaning of <c>not_applicable</c>.</b> Never defined. The single frontend
    /// implementation already overloads it for two different situations - a terminal case and
    /// a case with no deadlines - so it cannot be treated as a settled semantic.</item>
    /// </list>
    ///
    /// <para>Because none of the seven is provable, Support computes no deadline, asserts no
    /// compliance state, and reports the one declared value that makes no compliance claim.
    /// Caller-declared <c>firstResponseDueAt</c> and <c>resolutionDueAt</c> are still stored
    /// and returned verbatim, so no client-supplied fact is lost and the projection can be
    /// implemented later without a data migration. See the Support Core section of
    /// CURRENT_IMPLEMENTATION_AUTHORITY.md.</para>
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
