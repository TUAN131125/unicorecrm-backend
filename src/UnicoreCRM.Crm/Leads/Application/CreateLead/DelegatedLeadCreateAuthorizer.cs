using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.CreateLead;

/// <summary>
/// The only application boundary that can issue delegated inbound Lead-create authorization.
/// Its contract deliberately exposes no capability argument: every evaluation is for
/// <c>leads.create</c>.
/// </summary>
internal interface IDelegatedLeadCreateAuthorizer
{
    Task<DelegatedLeadIngressAuthorizationResult> AuthorizeAsync(
        TrustedWorkspaceContext trustedWorkspace,
        string delegatedSubjectId,
        string correlationId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Request-local proof that AccessControl allowed <c>leads.create</c> for the exact trusted
/// Workspace membership and delegated member. The proof has no serialization contract and is
/// created only by its nested authorizer immediately after the canonical AccessControl evaluation.
/// </summary>
internal sealed class DelegatedLeadIngressAuthorization
{
    private DelegatedLeadIngressAuthorization(
        TrustedWorkspaceContext trusted,
        string delegatedSubjectId)
    {
        Trusted = trusted;
        DelegatedSubjectId = delegatedSubjectId;
    }

    internal TrustedWorkspaceContext Trusted { get; }

    internal string DelegatedSubjectId { get; }

    /// <summary>
    /// Nested so this implementation alone can invoke the proof's private constructor.
    /// </summary>
    internal sealed class Authorizer(IDelegatedAccessAuthorizer accessAuthorizer)
        : IDelegatedLeadCreateAuthorizer
    {
        public async Task<DelegatedLeadIngressAuthorizationResult> AuthorizeAsync(
            TrustedWorkspaceContext trustedWorkspace,
            string delegatedSubjectId,
            string correlationId,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(trustedWorkspace);
            ArgumentException.ThrowIfNullOrWhiteSpace(delegatedSubjectId);

            if (!string.Equals(
                    trustedWorkspace.MemberId,
                    delegatedSubjectId,
                    StringComparison.Ordinal))
            {
                return DelegatedLeadIngressAuthorizationResult.Denied(LeadErrors.AccessDenied().Code);
            }

            var decision = await accessAuthorizer.AuthorizeAsync(
                trustedWorkspace,
                LeadCapabilities.Create,
                correlationId,
                cancellationToken);
            if (!decision.IsAllowed)
                return DelegatedLeadIngressAuthorizationResult.Denied(decision.Code);

            if (!MatchesTrustedContext(decision.Context, trustedWorkspace))
                return DelegatedLeadIngressAuthorizationResult.Denied(LeadErrors.AccessDenied().Code);

            return DelegatedLeadIngressAuthorizationResult.Allowed(
                new DelegatedLeadIngressAuthorization(trustedWorkspace, delegatedSubjectId));
        }

        private static bool MatchesTrustedContext(
            AuthorizationContextDocument? context,
            TrustedWorkspaceContext trustedWorkspace) =>
            context is not null
            && string.Equals(context.WorkspaceId, trustedWorkspace.WorkspaceId, StringComparison.Ordinal)
            && string.Equals(context.AccountId, trustedWorkspace.AccountId, StringComparison.Ordinal)
            && string.Equals(context.MemberId, trustedWorkspace.MemberId, StringComparison.Ordinal)
            && string.Equals(context.MembershipId, trustedWorkspace.MembershipId, StringComparison.Ordinal);
    }
}

internal sealed record DelegatedLeadIngressAuthorizationResult(
    DelegatedLeadIngressAuthorization? Authorization,
    string Code)
{
    internal static DelegatedLeadIngressAuthorizationResult Allowed(
        DelegatedLeadIngressAuthorization authorization) =>
        new(authorization, "AUTHORIZED");

    internal static DelegatedLeadIngressAuthorizationResult Denied(string code) => new(null, code);
}
