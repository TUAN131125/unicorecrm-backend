using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.Workspace.Infrastructure.Persistence;

internal sealed class EfWorkspaceTimeZoneReader(WorkspaceDbContext db) : IWorkspaceTimeZoneReader
{
    public Task<string?> ReadTimeZoneAsync(string workspaceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workspaceId) || workspaceId.Length > 128) throw new ArgumentException("Workspace identity is invalid.", nameof(workspaceId));
        return db.BootstrapProjections.AsNoTracking().Where(x => x.WorkspaceId == workspaceId)
            .Select(x => x.TimeZone).SingleOrDefaultAsync(cancellationToken);
    }
}
