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
    void AddAccount(IdentityAccount account);
    void AddCredential(IdentityCredential credential);
    void AddSession(IdentitySession session);
    void AddIdempotency(IdentityIdempotencyRecord record);
    void AddAudit(IdentityAuditRecord record);
    void AddSecurityEvent(IdentitySecurityEvent securityEvent);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal interface IIdentityTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}
