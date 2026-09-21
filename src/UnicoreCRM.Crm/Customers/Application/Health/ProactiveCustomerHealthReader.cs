using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Customers.Infrastructure.Persistence;

namespace UnicoreCRM.Crm.Customers.Application.Health;

internal sealed class ProactiveCustomerHealthReader(
    CustomersDbContext db,
    CustomerHealthAssessmentService health) : IProactiveCustomerHealthReader
{
    public async Task<ProactiveCustomerHealthPage> ReadPageAsync(
        string workspaceId, string? cursor, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workspaceId) || workspaceId.Length > 128) throw new ArgumentException("Workspace identity is invalid.", nameof(workspaceId));
        var take = Math.Clamp(limit, 1, 250);
        var query = db.Customers.AsNoTracking().Where(x => x.WorkspaceId == workspaceId);
        if (!string.IsNullOrEmpty(cursor)) query = query.Where(x => string.Compare(x.CustomerId, cursor) > 0);
        var customers = await query.OrderBy(x => x.CustomerId).Take(take + 1).ToArrayAsync(cancellationToken);
        var page = customers.Take(take).ToArray();
        var assessments = await health.AssessSystemBatchAsync(workspaceId, page, cancellationToken);
        var items = page.Select(customer =>
        {
            var assessment = assessments[customer.CustomerId];
            return new ProactiveCustomerHealthFact(customer.CustomerId, customer.OwnerId, customer.Status,
                assessment?.HealthBand, assessment?.ChurnRisk, assessment?.ReasonCode, assessment?.AlgorithmVersion,
                customer.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }).ToArray();
        return new(items, customers.Length > take ? page[^1].CustomerId : null);
    }
}
