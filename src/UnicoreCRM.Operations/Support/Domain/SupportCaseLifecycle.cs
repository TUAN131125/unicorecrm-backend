namespace UnicoreCRM.Operations.Support.Domain;

/// <summary>
/// The frozen SupportCase transition table. It is transcribed verbatim from the canonical
/// design baseline "Business rules and lifecycle" section in
/// design-authority/canonical-design/modules/support.md:
///
///   new              -> in_progress, waiting_customer, cancelled
///   in_progress      -> waiting_customer, waiting_internal, resolved, cancelled
///   waiting_customer -> in_progress, resolved, cancelled
///   waiting_internal -> in_progress, resolved, cancelled
///   resolved         -> closed, reopened
///   closed           -> reopened
///   cancelled        -> reopened
///   reopened         -> in_progress, waiting_customer, resolved
///
/// Same-state replay is admitted by the same section. Anything absent from the table fails
/// closed with the canonical <c>SUPPORT_CASE_INVALID_TRANSITION</c> error.
/// </summary>
internal static class SupportCaseLifecycle
{
    private static readonly IReadOnlyDictionary<SupportCaseStatus, SupportCaseStatus[]> Transitions =
        new Dictionary<SupportCaseStatus, SupportCaseStatus[]>
        {
            [SupportCaseStatus.New] = [SupportCaseStatus.InProgress, SupportCaseStatus.WaitingCustomer, SupportCaseStatus.Cancelled],
            [SupportCaseStatus.InProgress] = [SupportCaseStatus.WaitingCustomer, SupportCaseStatus.WaitingInternal, SupportCaseStatus.Resolved, SupportCaseStatus.Cancelled],
            [SupportCaseStatus.WaitingCustomer] = [SupportCaseStatus.InProgress, SupportCaseStatus.Resolved, SupportCaseStatus.Cancelled],
            [SupportCaseStatus.WaitingInternal] = [SupportCaseStatus.InProgress, SupportCaseStatus.Resolved, SupportCaseStatus.Cancelled],
            [SupportCaseStatus.Resolved] = [SupportCaseStatus.Closed, SupportCaseStatus.Reopened],
            [SupportCaseStatus.Closed] = [SupportCaseStatus.Reopened],
            [SupportCaseStatus.Cancelled] = [SupportCaseStatus.Reopened],
            [SupportCaseStatus.Reopened] = [SupportCaseStatus.InProgress, SupportCaseStatus.WaitingCustomer, SupportCaseStatus.Resolved]
        };

    /// <summary>The creation status. The canonical lifecycle table starts every case at <c>new</c>.</summary>
    internal const SupportCaseStatus Initial = SupportCaseStatus.New;

    internal static bool CanTransition(SupportCaseStatus from, SupportCaseStatus to) =>
        from == to || (Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to));
}
