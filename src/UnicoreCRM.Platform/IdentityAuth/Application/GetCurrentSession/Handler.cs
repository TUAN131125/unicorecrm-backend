using UnicoreCRM.Platform.IdentityAuth.Application.Common;
using UnicoreCRM.Platform.IdentityAuth.Contracts;
using UnicoreCRM.Platform.IdentityAuth.Domain;

namespace UnicoreCRM.Platform.IdentityAuth.Application.GetCurrentSession;

internal sealed class Handler(IIdentityAuthPersistence persistence, TimeProvider timeProvider)
{
    internal async Task<OperationResult<AuthSessionDocument>> HandleAsync(Query query, CancellationToken cancellationToken)
    {
        var session = await persistence.FindSessionAsync(query.SessionId, cancellationToken);
        if (session is null || !string.Equals(session.AccountId, query.AccountId, StringComparison.Ordinal))
            return OperationResult<AuthSessionDocument>.Failure(IdentityErrors.SessionInvalid());
        if (session.Status == SessionStatus.Revoked)
            return OperationResult<AuthSessionDocument>.Failure(IdentityErrors.SessionRevoked());
        if (!session.CanRefresh(timeProvider.GetUtcNow()))
            return OperationResult<AuthSessionDocument>.Failure(IdentityErrors.SessionExpired());
        var account = await persistence.FindAccountByIdAsync(query.AccountId, cancellationToken);
        if (account is null)
            return OperationResult<AuthSessionDocument>.Failure(IdentityErrors.SessionInvalid());

        persistence.AddAudit(new IdentityAuditRecord("getCurrentSession", "SUCCEEDED", account.AccountId, query.CorrelationId, timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
        return OperationResult<AuthSessionDocument>.Success(IdentityProjection.Session(account, session));
    }
}
