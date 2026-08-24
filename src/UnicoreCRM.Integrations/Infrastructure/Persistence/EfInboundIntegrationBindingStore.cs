using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Integrations.Application;
using UnicoreCRM.Integrations.Domain;

namespace UnicoreCRM.Integrations.Infrastructure.Persistence;

internal sealed class EfInboundIntegrationBindingStore(IntegrationsDbContext dbContext)
    : IInboundIntegrationBindingStore
{
    public Task<InboundIntegrationBinding?> FindAsync(
        string integrationId,
        CancellationToken cancellationToken) =>
        dbContext.InboundBindings
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.IntegrationId == integrationId, cancellationToken);
}
