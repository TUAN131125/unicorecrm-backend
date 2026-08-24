using UnicoreCRM.Platform.IdentityAuth.Application.Common;
using UnicoreCRM.Platform.IdentityAuth.Contracts;
using UnicoreCRM.Platform.IdentityAuth.Domain;

namespace UnicoreCRM.Platform.IdentityAuth.Application.RefreshSession;

internal sealed class Handler(
    IIdentityAuthPersistence persistence,
    IIdentityTokenIssuer tokenIssuer,
    IRefreshTokenProtector refreshTokens,
    IIdentityRequestFingerprinter fingerprinter,
    IIdentitySessionPolicy sessionPolicy,
    TimeProvider timeProvider)
{
    internal async Task<OperationResult<(AuthenticatedSessionResponse Response, string RefreshToken)>> HandleAsync(Command command, CancellationToken cancellationToken)
    {
        if (!refreshTokens.HasExpectedShape(command.RefreshToken, out var sessionId))
            return OperationResult<(AuthenticatedSessionResponse, string)>.Failure(IdentityErrors.SessionInvalid());

        var fingerprint = fingerprinter.Create(command.RefreshToken);
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var prior = await persistence.FindIdempotencyAsync("refreshSession", command.Metadata.IdempotencyKey, cancellationToken);
        if (prior is not null)
        {
            if (!string.Equals(prior.Fingerprint, fingerprint, StringComparison.Ordinal))
                return OperationResult<(AuthenticatedSessionResponse, string)>.Failure(IdentityErrors.IdempotencyReused());
            var replaySession = await persistence.FindSessionAsync(prior.ResourceId, cancellationToken);
            var replayAccount = replaySession is null ? null : await persistence.FindAccountByIdAsync(replaySession.AccountId, cancellationToken);
            if (replaySession is null || replayAccount is null || !replaySession.CanRefresh(timeProvider.GetUtcNow()))
                return OperationResult<(AuthenticatedSessionResponse, string)>.Failure(IdentityErrors.SessionExpired());
            return OperationResult<(AuthenticatedSessionResponse, string)>.Success(Project(replayAccount, replaySession));
        }

        var session = await persistence.FindSessionAsync(sessionId, cancellationToken);
        if (session is null || !refreshTokens.Matches(command.RefreshToken, session.RefreshTokenHash))
            return OperationResult<(AuthenticatedSessionResponse, string)>.Failure(IdentityErrors.SessionInvalid());
        var now = timeProvider.GetUtcNow();
        if (session.Status == SessionStatus.Revoked)
            return OperationResult<(AuthenticatedSessionResponse, string)>.Failure(IdentityErrors.SessionRevoked());
        if (!session.CanRefresh(now))
            return OperationResult<(AuthenticatedSessionResponse, string)>.Failure(IdentityErrors.SessionExpired());

        var account = await persistence.FindAccountByIdAsync(session.AccountId, cancellationToken);
        if (account is null)
            return OperationResult<(AuthenticatedSessionResponse, string)>.Failure(IdentityErrors.SessionInvalid());

        session.Rotate(now, sessionPolicy.IdleLifetime);
        var newRefreshToken = refreshTokens.Create(session);
        session.SetCurrentRefreshHash(refreshTokens.Hash(newRefreshToken));
        persistence.AddIdempotency(new IdentityIdempotencyRecord("refreshSession", command.Metadata.IdempotencyKey, fingerprint, session.SessionId, now));
        persistence.AddAudit(new IdentityAuditRecord("refreshSession", "SUCCEEDED", account.AccountId, command.Metadata.CorrelationId, now));
        persistence.AddSecurityEvent(new IdentitySecurityEvent("IDENTITY_SESSION_REFRESHED", account.AccountId, command.Metadata.CorrelationId, now));
        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return OperationResult<(AuthenticatedSessionResponse, string)>.Success(Project(account, session));
    }

    private (AuthenticatedSessionResponse, string) Project(IdentityAccount account, IdentitySession session)
    {
        var accessToken = tokenIssuer.Issue(account, session);
        return (new AuthenticatedSessionResponse(IdentityProjection.Session(account, session), accessToken.Value, accessToken.ExpiresAt), refreshTokens.Create(session));
    }
}
