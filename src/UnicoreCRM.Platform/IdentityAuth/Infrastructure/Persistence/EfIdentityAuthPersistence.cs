using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UnicoreCRM.Platform.IdentityAuth.Application.Common;
using UnicoreCRM.Platform.IdentityAuth.Domain;

namespace UnicoreCRM.Platform.IdentityAuth.Infrastructure.Persistence;

internal sealed class EfIdentityAuthPersistence(IdentityAuthDbContext dbContext) : IIdentityAuthPersistence
{
    public async Task<IIdentityTransaction> BeginSerializableAsync(CancellationToken cancellationToken) =>
        new EfIdentityTransaction(await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken));

    public Task<IdentityAccount?> FindAccountByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        dbContext.Accounts.SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

    public Task<IdentityAccount?> FindAccountByIdAsync(string accountId, CancellationToken cancellationToken) =>
        dbContext.Accounts.SingleOrDefaultAsync(x => x.AccountId == accountId, cancellationToken);

    public Task<IdentityCredential?> FindCredentialAsync(string accountId, CancellationToken cancellationToken) =>
        dbContext.Credentials.SingleOrDefaultAsync(x => x.AccountId == accountId, cancellationToken);

    public Task<IdentitySession?> FindSessionAsync(string sessionId, CancellationToken cancellationToken) =>
        dbContext.Sessions.SingleOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken);

    public Task<IdentityIdempotencyRecord?> FindIdempotencyAsync(string operation, string key, CancellationToken cancellationToken) =>
        dbContext.IdempotencyRecords.SingleOrDefaultAsync(x => x.Operation == operation && x.Key == key, cancellationToken);

    public void AddAccount(IdentityAccount account) => dbContext.Accounts.Add(account);
    public void AddCredential(IdentityCredential credential) => dbContext.Credentials.Add(credential);
    public void AddSession(IdentitySession session) => dbContext.Sessions.Add(session);
    public void AddIdempotency(IdentityIdempotencyRecord record) => dbContext.IdempotencyRecords.Add(record);
    public void AddAudit(IdentityAuditRecord record) => dbContext.AuditRecords.Add(record);
    public void AddSecurityEvent(IdentitySecurityEvent securityEvent) => dbContext.SecurityEvents.Add(securityEvent);
    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    private sealed class EfIdentityTransaction(IDbContextTransaction transaction) : IIdentityTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
