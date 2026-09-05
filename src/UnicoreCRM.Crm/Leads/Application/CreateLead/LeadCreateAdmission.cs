using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.CreateLead;

/// <summary>
/// The closed set of ways a Lead creation may be admitted. It exists so that "which security model
/// applies" is a value the type system forces every caller to state, rather than the absence of an
/// optional argument. A nullable <c>LeadAccess?</c> previously carried that meaning, which made
/// forgetting to pass a decision indistinguishable from deliberately skipping enforcement.
///
/// <para>Both cases are sealed and private-constructed, so no third admission model can appear
/// without an authority decision and a change here.</para>
/// </summary>
internal abstract class LeadCreateAdmission
{
    private LeadCreateAdmission(TrustedWorkspaceContext trusted) => Trusted = trusted;

    internal TrustedWorkspaceContext Trusted { get; }

    /// <summary>Resolves the owner used by this admission model before profile validation.</summary>
    internal abstract string? ResolveOwnerId(string? requestedOwnerId);

    /// <summary>Refuses the creation when the admitted model applies field-write policy and the request writes a field the caller may not write.</summary>
    internal abstract LeadOperationError? GuardCreateWrite(Domain.LeadProfile profile);

    /// <summary>Refuses a new execution when its owner or audit provenance is not bound to the admitted authority.</summary>
    internal abstract LeadOperationError? GuardExecutionBinding(
        Domain.LeadProfile profile,
        LeadCommandMetadata metadata);

    /// <summary>Projects the outgoing response through the admitted model's field-read policy.</summary>
    internal abstract LeadMutationResponse Project(LeadMutationResponse response);

    /// <summary>
    /// Captures the interested-product snapshots this admission model is allowed to capture.
    /// Capture reads Products-owned facts, so which models may perform it is an authorization
    /// question and belongs to the admission model rather than to the shared execution.
    /// </summary>
    internal abstract Task<LeadInterestedProductResolution.Outcome> CaptureInterestedProductsAsync(
        LeadInterestedProductResolution resolution,
        IReadOnlyList<Domain.LeadInterestedProductIntent> intents,
        CancellationToken cancellationToken);

    internal static LeadCreateAdmission Interactive(LeadAccess access) => new InteractiveAdmission(access);

    internal static LeadCreateAdmission DelegatedIngress(DelegatedLeadIngressAuthorization authorization) =>
        new DelegatedIngressAdmission(authorization);

    /// <summary>
    /// An authenticated member creating a Lead through the Leads API. The full interactive model
    /// applies: the AccessControl decision governs which fields may be written, and the response is
    /// projected through the same decision.
    /// </summary>
    private sealed class InteractiveAdmission(LeadAccess access) : LeadCreateAdmission(access.Trusted)
    {
        internal override string? ResolveOwnerId(string? requestedOwnerId) =>
            string.IsNullOrWhiteSpace(requestedOwnerId) ? Trusted.MemberId : requestedOwnerId;

        internal override LeadOperationError? GuardCreateWrite(Domain.LeadProfile profile) =>
            LeadFieldSecurity.GuardCreateWrite(access.Authorization, profile);

        internal override LeadOperationError? GuardExecutionBinding(
            Domain.LeadProfile profile,
            LeadCommandMetadata metadata) => null;

        internal override LeadMutationResponse Project(LeadMutationResponse response) =>
            response with { Result = LeadFieldSecurity.Project(response.Result, access.Authorization) };

        /// <summary>
        /// The interactive model captures normally. Products evaluates <c>products.read</c> at its
        /// own boundary for the acting member, so a member who may create Leads but may not read the
        /// catalog cannot obtain Product facts through a Lead.
        /// </summary>
        internal override Task<LeadInterestedProductResolution.Outcome> CaptureInterestedProductsAsync(
            LeadInterestedProductResolution resolution,
            IReadOnlyList<Domain.LeadInterestedProductIntent> intents,
            CancellationToken cancellationToken) =>
            resolution.ResolveForCreateAsync(intents, cancellationToken);
    }

    /// <summary>
    /// The delegated Integration ingress. Current authority admits exactly one authorization concern
    /// for this path - "AccessControl evaluates the member's actual server-side `leads.create`
    /// capability through a delegated internal authorization contract" - and admits no field-security
    /// concern for it at all. The payload is a closed extension shape that cannot carry a Workspace,
    /// member, owner or capability, and the owner is taken from the binding rather than the sender.
    ///
    /// <para>Whether the delegated subject's field-security policy should additionally govern this
    /// path is an <c>AUTHORITY_GAP</c>. It is deliberately not answered here: applying interactive
    /// field policy would silently change admitted integration behaviour, and declaring the path
    /// exempt would be an equally unproven claim. Current behaviour is preserved and the gap is
    /// recorded, but it is now a named, single, auditable admission model rather than a null.</para>
    /// </summary>
    private sealed class DelegatedIngressAdmission(DelegatedLeadIngressAuthorization authorization)
        : LeadCreateAdmission(authorization.Trusted)
    {
        internal override string? ResolveOwnerId(string? requestedOwnerId) => requestedOwnerId;

        internal override LeadOperationError? GuardCreateWrite(Domain.LeadProfile profile) => null;

        internal override LeadOperationError? GuardExecutionBinding(
            Domain.LeadProfile profile,
            LeadCommandMetadata metadata) =>
            string.Equals(profile.OwnerId, authorization.DelegatedSubjectId, StringComparison.Ordinal)
            && string.Equals(
                metadata.DelegatedSubjectId,
                authorization.DelegatedSubjectId,
                StringComparison.Ordinal)
                ? null
                : LeadErrors.AccessDenied();

        internal override LeadMutationResponse Project(LeadMutationResponse response) => response;

        /// <summary>
        /// The delegated ingress stays fail-closed for interested products. Its admitted
        /// authorization concern is exactly one delegated <c>leads.create</c> evaluation; no
        /// delegated <c>products.read</c> is admitted for this path, and capturing Product facts
        /// without it would be an unauthorized cross-owner disclosure driven by an external sender.
        /// An empty or omitted collection is unaffected, so current webhook behaviour is unchanged.
        /// </summary>
        internal override Task<LeadInterestedProductResolution.Outcome> CaptureInterestedProductsAsync(
            LeadInterestedProductResolution resolution,
            IReadOnlyList<Domain.LeadInterestedProductIntent> intents,
            CancellationToken cancellationToken) =>
            Task.FromResult(intents.Count == 0
                ? new LeadInterestedProductResolution.Outcome([], null)
                : new LeadInterestedProductResolution.Outcome(
                    null,
                    LeadErrors.Validation(new Dictionary<string, string[]>
                    {
                        ["interestedProducts"] =
                        [
                            "interestedProducts are not available on the delegated inbound ingress path."
                        ]
                    })));
    }
}
