using UnicoreCRM.Platform.IdentityAuth.Application.Common;
using UnicoreCRM.Platform.IdentityAuth.Contracts;
using UnicoreCRM.Platform.IdentityAuth.Domain;

namespace UnicoreCRM.Platform.IdentityAuth.Application.SignOut;

internal sealed class Handler(IIdentityAuthPersistence persistence, IIdentityRequestFingerprinter fingerprinter, TimeProvider timeProvider)
{
    internal async Task<OperationResult<SessionRevocationResponse>> HandleAsync(Command command, CancellationToken cancellationToken)
    {
        var validation = Validator.Validate(command);
        if (validation.Count != 0)
            return OperationResult<SessionRevocationResponse>.Failure(IdentityErrors.Validation(validation));
        var fingerprint = fingerprinter.Create(command.AccountId, command.SessionId, command.Reason);
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var prior = await persistence.FindIdempotencyAsync("signOut", command.Metadata.IdempotencyKey, cancellationToken);
        if (prior is not null)
        {
            if (!string.Equals(prior.Fingerprint, fingerprint, StringComparison.Ordinal))
                return OperationResult<SessionRevocationResponse>.Failure(IdentityErrors.IdempotencyReused());
            var replay = await persistence.FindSessionAsync(prior.ResourceId, cancellationToken);
            return replay?.RevokedAt is null
                ? OperationResult<SessionRevocationResponse>.Failure(IdentityErrors.SessionInvalid())
                : OperationResult<SessionRevocationResponse>.Success(new SessionRevocationResponse(replay.SessionId, replay.RevokedAt.Value));
        }

        var session = await persistence.FindSessionAsync(command.SessionId, cancellationToken);
        if (session is null || !string.Equals(session.AccountId, command.AccountId, StringComparison.Ordinal))
            return OperationResult<SessionRevocationResponse>.Failure(IdentityErrors.SessionInvalid());
        var now = timeProvider.GetUtcNow();
        session.Revoke(now, command.Reason);
        persistence.AddIdempotency(new IdentityIdempotencyRecord("signOut", command.Metadata.IdempotencyKey, fingerprint, session.SessionId, now));
        persistence.AddAudit(new IdentityAuditRecord("signOut", "SUCCEEDED", command.AccountId, command.Metadata.CorrelationId, now));
        persistence.AddSecurityEvent(new IdentitySecurityEvent("IDENTITY_SESSION_REVOKED", command.AccountId, command.Metadata.CorrelationId, now));
        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return OperationResult<SessionRevocationResponse>.Success(new SessionRevocationResponse(session.SessionId, session.RevokedAt!.Value));
    }
}
