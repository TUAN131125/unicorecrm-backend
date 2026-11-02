using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.AccessControl.Domain;

namespace UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence;

internal sealed class EfServiceAccessAuthorizer(AccessControlDbContext db, TimeProvider timeProvider) : IServiceAccessAuthorizer
{
    public async Task<ServiceAccessAuthorizationDecision> AuthorizeAsync(string workspaceId, string servicePrincipalId,
        AccessRequirement requirement, string correlationId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(servicePrincipalId);
        ArgumentNullException.ThrowIfNull(requirement);
        var allowed = await db.WorkspaceServiceCapabilityGrants.AsNoTracking().AnyAsync(x =>
            x.WorkspaceId == workspaceId && x.ServicePrincipalId == servicePrincipalId &&
            x.Capability == requirement.Capability, cancellationToken);
        var evidence = new ServiceAuthorizationDecisionRecord(workspaceId, servicePrincipalId,
            requirement.Capability, allowed, correlationId, timeProvider.GetUtcNow());
        db.ServiceAuthorizationDecisions.Add(evidence);
        await db.SaveChangesAsync(cancellationToken);
        return new(allowed, allowed ? "AUTHORIZED" : "ACCESS_DENIED", evidence.DecisionId);
    }
}
