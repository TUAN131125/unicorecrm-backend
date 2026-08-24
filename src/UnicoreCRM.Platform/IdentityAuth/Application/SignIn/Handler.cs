using UnicoreCRM.Platform.IdentityAuth.Application.Common;
using UnicoreCRM.Platform.IdentityAuth.Contracts;
using UnicoreCRM.Platform.IdentityAuth.Domain;

namespace UnicoreCRM.Platform.IdentityAuth.Application.SignIn;

internal sealed class Handler(
    IIdentityAuthPersistence persistence,
    IIdentityPasswordHasher passwordHasher,
    IIdentityTokenIssuer tokenIssuer,
    IRefreshTokenProtector refreshTokens,
    IIdentityRequestFingerprinter fingerprinter,
    IIdentitySessionPolicy sessionPolicy,
    TimeProvider timeProvider)
{
    internal async Task<OperationResult<(AuthenticatedSessionResponse Response, string RefreshToken)>> HandleAsync(Command command, CancellationToken cancellationToken)
    {
        var validation = Validator.Validate(command);
        if (validation.Count != 0)
            return OperationResult<(AuthenticatedSessionResponse, string)>.Failure(IdentityErrors.Validation(validation));

        var fingerprint = fingerprinter.Create(command.Email.ToUpperInvariant(), command.Password, command.DeviceLabel);
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var prior = await persistence.FindIdempotencyAsync("signIn", command.Metadata.IdempotencyKey, cancellationToken);
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

        var account = await persistence.FindAccountByEmailAsync(command.Email.Trim().ToUpperInvariant(), cancellationToken);
        if (account is null)
        {
            passwordHasher.ConsumeUnknownPassword(command.Password);
            return OperationResult<(AuthenticatedSessionResponse, string)>.Failure(IdentityErrors.InvalidCredentials());
        }

        var credential = await persistence.FindCredentialAsync(account.AccountId, cancellationToken);
        if (credential is null || !passwordHasher.Verify(account, credential.PasswordHash, command.Password))
            return OperationResult<(AuthenticatedSessionResponse, string)>.Failure(IdentityErrors.InvalidCredentials());
        if (account.Status == AccountStatus.Suspended)
            return OperationResult<(AuthenticatedSessionResponse, string)>.Failure(IdentityErrors.AccountSuspended());
        if (account.Status != AccountStatus.Active)
            return OperationResult<(AuthenticatedSessionResponse, string)>.Failure(IdentityErrors.EmailNotVerified());

        var now = timeProvider.GetUtcNow();
        var session = new IdentitySession(
            account.AccountId,
            string.Empty,
            string.IsNullOrWhiteSpace(command.DeviceLabel) ? "Unknown device" : command.DeviceLabel.Trim(),
            command.Metadata.UserAgent,
            now,
            now.Add(sessionPolicy.IdleLifetime),
            now.Add(sessionPolicy.AbsoluteLifetime));
        var refreshToken = refreshTokens.Create(session);
        session.SetInitialRefreshHash(refreshTokens.Hash(refreshToken));
        persistence.AddSession(session);
        persistence.AddIdempotency(new IdentityIdempotencyRecord("signIn", command.Metadata.IdempotencyKey, fingerprint, session.SessionId, now));
        persistence.AddAudit(new IdentityAuditRecord("signIn", "SUCCEEDED", account.AccountId, command.Metadata.CorrelationId, now));
        persistence.AddSecurityEvent(new IdentitySecurityEvent("IDENTITY_SESSION_CREATED", account.AccountId, command.Metadata.CorrelationId, now));
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
