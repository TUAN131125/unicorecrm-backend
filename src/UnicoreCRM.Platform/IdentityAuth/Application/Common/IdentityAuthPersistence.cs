using UnicoreCRM.Platform.IdentityAuth.Domain;

namespace UnicoreCRM.Platform.IdentityAuth.Application.Common;

internal interface IIdentityAuthPersistence
{
    Task<IIdentityTransaction> BeginSerializableAsync(CancellationToken cancellationToken);
    Task<IdentityAccount?> FindAccountByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task<IdentityAccount?> FindAccountByIdAsync(string accountId, CancellationToken cancellationToken);
    Task<IdentityCredential?> FindCredentialAsync(string accountId, CancellationToken cancellationToken);
    Task<IdentitySession?> FindSessionAsync(string sessionId, CancellationToken cancellationToken);
    Task<IdentityIdempotencyRecord?> FindIdempotencyAsync(string operation, string key, CancellationToken cancellationToken);
    Task<IReadOnlyList<IdentityEmailVerificationChallenge>> ListOutstandingEmailVerificationChallengesAsync(string accountId, CancellationToken cancellationToken);

    /// <summary>
    /// The still-undelivered outbox messages of the given challenges. Read inside the transaction that
    /// is about to invalidate those challenges, so the same serialisable transaction that revokes a
    /// code also decides the fate of the message carrying it.
    /// </summary>
    Task<IReadOnlyList<IdentityEmailOutboxMessage>> ListUndeliveredEmailOutboxMessagesAsync(IReadOnlyCollection<string> challengeIds, CancellationToken cancellationToken);
    void AddAccount(IdentityAccount account);
    void AddCredential(IdentityCredential credential);
    void AddSession(IdentitySession session);
    void AddEmailVerificationChallenge(IdentityEmailVerificationChallenge challenge);
    void AddEmailOutboxMessage(IdentityEmailOutboxMessage message);
    void AddIdempotency(IdentityIdempotencyRecord record);
    void AddAudit(IdentityAuditRecord record);
    void AddSecurityEvent(IdentitySecurityEvent securityEvent);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal interface IIdentityTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}
