using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.CreateLead;

/// <summary>
/// Proof that the delegated inbound-Lead path has been authorized. It cannot be constructed from
/// nothing: the only factory requires an <see cref="AccessAuthorizationDecision"/> that actually
/// allowed the delegated <c>leads.create</c> evaluation, so a caller cannot manufacture delegated
/// admission by passing a flag or omitting a parameter.
/// </summary>
internal sealed class DelegatedLeadIngressAuthorization
{
    private DelegatedLeadIngressAuthorization(TrustedWorkspaceContext trusted, string delegatedSubjectId)
    {
        Trusted = trusted;
        DelegatedSubjectId = delegatedSubjectId;
    }

    internal TrustedWorkspaceContext Trusted { get; }

    /// <summary>The Workspace member the Integration executes through. It is server-resolved from the binding, never supplied by the sender.</summary>
    internal string DelegatedSubjectId { get; }

    /// <summary>
    /// Produces the admission proof, or <c>null</c> when the delegated evaluation did not allow the
    /// capability. Returning null rather than throwing keeps the caller's denial path explicit.
    /// </summary>
    internal static DelegatedLeadIngressAuthorization? FromAllowedDecision(
        AccessAuthorizationDecision decision,
        TrustedWorkspaceContext trusted,
        string delegatedSubjectId)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(trusted);
        ArgumentException.ThrowIfNullOrWhiteSpace(delegatedSubjectId);
        if (!decision.IsAllowed)
            return null;
        return string.Equals(trusted.MemberId, delegatedSubjectId, StringComparison.Ordinal)
            ? new DelegatedLeadIngressAuthorization(trusted, delegatedSubjectId)
            : null;
    }
}

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

    /// <summary>Refuses the creation when the admitted model applies field-write policy and the request writes a field the caller may not write.</summary>
    internal abstract LeadOperationError? GuardCreateWrite(Domain.LeadProfile profile);

    /// <summary>Projects the outgoing response through the admitted model's field-read policy.</summary>
    internal abstract LeadMutationResponse Project(LeadMutationResponse response);

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
        internal override LeadOperationError? GuardCreateWrite(Domain.LeadProfile profile) =>
            LeadFieldSecurity.GuardCreateWrite(access.Authorization, profile);

        internal override LeadMutationResponse Project(LeadMutationResponse response) =>
            response with { Result = LeadFieldSecurity.Project(response.Result, access.Authorization) };
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
        internal override LeadOperationError? GuardCreateWrite(Domain.LeadProfile profile) => null;

        internal override LeadMutationResponse Project(LeadMutationResponse response) => response;
    }
}
