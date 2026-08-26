using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.Workspace.Infrastructure.Persistence;

internal sealed class EfWorkspaceCurrencyConfigurationReader(WorkspaceDbContext dbContext)
    : IWorkspaceCurrencyConfigurationReader
{
    public Task<WorkspaceCurrencyConfiguration?> FindAsync(
        string workspaceId,
        CancellationToken cancellationToken) =>
        dbContext.BootstrapProjections
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId)
            .Select(item => new WorkspaceCurrencyConfiguration(item.BaseCurrency, item.ConfigurationVersion))
            .SingleOrDefaultAsync(cancellationToken);
}
