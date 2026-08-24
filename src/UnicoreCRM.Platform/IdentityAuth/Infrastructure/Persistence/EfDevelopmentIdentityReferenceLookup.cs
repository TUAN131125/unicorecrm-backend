using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.IdentityAuth.Contracts;
using UnicoreCRM.Platform.IdentityAuth.Domain;

namespace UnicoreCRM.Platform.IdentityAuth.Infrastructure.Persistence;

internal sealed class EfDevelopmentIdentityReferenceLookup(IdentityAuthDbContext dbContext) : IDevelopmentIdentityReferenceLookup
{
    public Task<DevelopmentIdentityReference?> FindActiveByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();
        return dbContext.Accounts
            .AsNoTracking()
            .Where(account => account.NormalizedEmail == normalizedEmail && account.Status == AccountStatus.Active)
            .Select(account => new DevelopmentIdentityReference(account.AccountId, account.MemberId))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
