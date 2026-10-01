using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.Workspace.Application.Common;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.Workspace.Infrastructure.Persistence;

internal sealed class EfLeadHandoverPolicyReader(WorkspaceDbContext db) : ILeadHandoverPolicyReader
{
    public async Task<LeadHandoverPolicy?> FindAsync(string workspaceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workspaceId) || workspaceId.Length > 128)
            throw new ArgumentException("Workspace identity is invalid.", nameof(workspaceId));

        var blueprintJson = await db.StudioConfigurations.AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId)
            .Select(item => item.BlueprintJson)
            .SingleOrDefaultAsync(cancellationToken);
        if (blueprintJson is null) return null;

        var hours = StudioJson.Deserialize<WorkspaceBlueprintDocument>(blueprintJson).Workflow.HandoverAcceptanceSlaHours;
        if (hours is < 1 or > 168)
            throw new InvalidOperationException("Stored workspace handover acceptance SLA is invalid.");
        return new LeadHandoverPolicy(hours);
    }
}
