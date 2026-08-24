using UnicoreCRM.Platform.IdentityAuth.Contracts;
using UnicoreCRM.Platform.IdentityAuth.Domain;

namespace UnicoreCRM.Platform.IdentityAuth.Application.Common;

internal static class IdentityProjection
{
    internal static UserAccountDocument Account(IdentityAccount account) => new(
        account.AccountId,
        account.Email,
        account.DisplayName,
        account.Status switch
        {
            AccountStatus.Active => AccountStatusDocument.Active,
            AccountStatus.Suspended => AccountStatusDocument.Suspended,
            _ => AccountStatusDocument.PendingVerification
        },
        account.CreatedAt,
        account.EmailVerifiedAt);

    internal static AuthSessionDocument Session(IdentityAccount account, IdentitySession session) => new(
        session.SessionId,
        new AuthenticatedPrincipalDocument(account.AccountId, account.MemberId, account.Email, account.DisplayName),
        session.Status == SessionStatus.Active ? SessionStatusDocument.Active : SessionStatusDocument.Revoked,
        session.IssuedAt,
        session.LastSeenAt,
        session.IdleExpiresAt,
        session.AbsoluteExpiresAt,
        session.RefreshCounter,
        "AAL1",
        new DeviceDocument(session.DeviceId, session.DeviceLabel, session.LastSeenAt, session.UserAgent),
        null,
        session.RevokedAt,
        session.RevokeReason);
}
