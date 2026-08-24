using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.IdentityAuth.Contracts;
using UnicoreCRM.Platform.IdentityAuth.Domain;

namespace UnicoreCRM.Platform.IdentityAuth.Infrastructure.Persistence;

internal sealed class EfAuthenticatedIdentityReferenceLookup(IdentityAuthDbContext dbContext)
    : IAuthenticatedIdentityReferenceLookup
{
    public Task<AuthenticatedIdentityReference?> FindActiveAsync(
        string accountId,
        string memberId,
        CancellationToken cancellationToken) =>
        dbContext.Accounts
            .AsNoTracking()
            .Where(account =>
                account.AccountId == accountId
                && account.MemberId == memberId
                && account.Status == AccountStatus.Active)
            .Select(account => new AuthenticatedIdentityReference(account.AccountId, account.MemberId))
            .SingleOrDefaultAsync(cancellationToken);
}
