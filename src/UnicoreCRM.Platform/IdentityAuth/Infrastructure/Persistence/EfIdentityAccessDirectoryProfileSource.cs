using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.IdentityAuth.Contracts;
using UnicoreCRM.Platform.IdentityAuth.Domain;

namespace UnicoreCRM.Platform.IdentityAuth.Infrastructure.Persistence;

internal sealed class EfIdentityAccessDirectoryProfileSource(IdentityAuthDbContext dbContext)
    : IIdentityAccessDirectoryProfileSource
{
    public async Task<IReadOnlyList<IdentityAccessDirectoryProfile>> ReadAsync(
        IReadOnlyList<string> accountIds,
        CancellationToken cancellationToken)
    {
        if (accountIds.Count == 0)
            return [];

        var profiles = await dbContext.Accounts
            .AsNoTracking()
            .Where(item => accountIds.Contains(item.AccountId))
            .OrderBy(item => item.AccountId)
            .Select(item => new
            {
                item.AccountId,
                item.DisplayName,
                item.Email,
                item.Status,
                item.CreatedAt
            })
            .ToArrayAsync(cancellationToken);

        return profiles.Select(item => new IdentityAccessDirectoryProfile(
            item.AccountId,
            item.DisplayName,
            item.Email,
            item.Status switch
            {
                AccountStatus.Active => "ACTIVE",
                AccountStatus.Suspended => "SUSPENDED",
                _ => "PENDING_VERIFICATION"
            },
            item.CreatedAt)).ToArray();
    }
}
