using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.Workspace.Infrastructure.Persistence;

internal sealed class EfEffectiveWorkspaceBaseCurrencyReader(WorkspaceDbContext dbContext)
    : IEffectiveWorkspaceBaseCurrencyReader
{
    public Task<EffectiveWorkspaceBaseCurrency?> FindAsync(
        string workspaceId,
        CancellationToken cancellationToken) =>
        dbContext.BootstrapProjections
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId)
            .Select(item => new EffectiveWorkspaceBaseCurrency(item.BaseCurrency, item.ConfigurationVersion))
            .SingleOrDefaultAsync(cancellationToken);
}
