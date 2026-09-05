using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using UnicoreCRM.Platform.IdentityAuth.Application.Common;
using UnicoreCRM.Platform.IdentityAuth.Domain;

namespace UnicoreCRM.Platform.IdentityAuth.Infrastructure.Persistence;

internal sealed class DevelopmentIdentityBootstrap(
    IHostEnvironment environment,
    IdentityAuthDbContext dbContext,
    IIdentityPasswordHasher passwordHasher,
    IOptions<IdentityAuthOptions> options)
{
    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        var bootstrap = options.Value.DevelopmentBootstrap;
        if (!environment.IsDevelopment() || !bootstrap.Enabled)
            return;
        if (string.IsNullOrWhiteSpace(bootstrap.Email) || bootstrap.Password.Length is < 8 or > 1024)
            throw new InvalidOperationException("Development bootstrap requires a valid email and an 8-1024 character password from external configuration.");

        var normalizedEmail = bootstrap.Email.Trim().ToUpperInvariant();
        if (await dbContext.Accounts.AnyAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken))
            return;

        var now = TimeProvider.System.GetUtcNow();
        var account = new IdentityAccount(bootstrap.Email.Trim(), bootstrap.DisplayName.Trim(), now, true);
        dbContext.Accounts.Add(account);
        dbContext.Credentials.Add(new IdentityCredential(account.AccountId, passwordHasher.Hash(account, bootstrap.Password), now));
        dbContext.AuditRecords.Add(new IdentityAuditRecord("developmentBootstrap", "SUCCEEDED", account.AccountId, "development-bootstrap", now));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

}
