using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.Workspace.Application.Common;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.Workspace.Infrastructure.Persistence;

internal sealed class EfQuoteWorkspacePolicyReader(WorkspaceDbContext db) : IQuoteWorkspacePolicyReader
{
    public async Task<QuoteWorkspacePolicy?> ReadAsync(string workspaceId, CancellationToken cancellationToken)
    {
        var row = await db.StudioConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId, cancellationToken);
        if (row is null) return null;
        var locale = StudioJson.Deserialize<WorkspaceLocaleRegionDocument>(row.LocaleRegionJson);
        var features = StudioJson.Deserialize<WorkspaceFeatureUsageDocument>(row.FeaturesJson);
        var blueprint = StudioJson.Deserialize<WorkspaceBlueprintDocument>(row.BlueprintJson);
        return new(features.Quotes && blueprint.Workflow.QuoteUsageMode != "DISABLED", locale.Currencies.EnabledCurrencies, row.Revision);
    }
}
