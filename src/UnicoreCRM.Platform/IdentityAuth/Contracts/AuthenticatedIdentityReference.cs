namespace UnicoreCRM.Platform.IdentityAuth.Contracts;

/// <summary>
/// The narrow cross-owner IdentityAuth surface used by multi-owner workflows that must
/// re-verify the authenticated principal against authoritative IdentityAuth state before
/// mutating foreign-owner records. It exposes no credential, session or profile state and
/// returns a value only for an account whose current status is active.
/// </summary>
public sealed record AuthenticatedIdentityReference(string AccountId, string MemberId);

public interface IAuthenticatedIdentityReferenceLookup
{
    Task<AuthenticatedIdentityReference?> FindActiveAsync(
        string accountId,
        string memberId,
        CancellationToken cancellationToken);
}
