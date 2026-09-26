using UnicoreCRM.Crm.Customers.Application.Common;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Customers.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Customers.Application.Health;

internal sealed class ProactiveCustomerAiContextReader(
    CustomerAuthorization authorization, ICustomersPersistence persistence,
    CustomerHealthAssessmentService health, TimeProvider clock) : IProactiveCustomerAiContextReader
{
    private static readonly IReadOnlyList<string> Fields = ["id", "customerCode", "status", "health"];
    private static readonly RecordAccessRepresentation Representation =
        RecordAccessRepresentation.Create("customer.proactiveSuggestion", "id", "customerCode", "status", "health");

    public async Task<ProactiveCustomerAiContextResult> ReadAsync(string customerId,
        CustomerAttentionRequestContext requestContext, CancellationToken cancellationToken)
    {
        var metadata = new CustomerRequestMetadata(requestContext.RequestId, requestContext.CorrelationId);
        var access = await authorization.AuthorizeAsync(metadata, CustomerCapabilities.View,
            cancellationToken, Representation, Fields);
        if (!access.IsSuccess) return new(false);
        var current = access.Value!;
        var policy = current.Authorization;
        if (!policy.CanRead("id") || !policy.CanRead("health")) return new(true);
        var customer = await persistence.ReadCustomerAsync(current.Trusted.WorkspaceId, customerId, cancellationToken);
        if (customer is null || customer.OwnerId != current.Trusted.MemberId
            || await authorization.EnforceRecordAsync(current, customer, "readProactiveCustomerAiContext", metadata, cancellationToken) is not null)
            return new(true);
        var assessment = await health.AssessAsync(current.Trusted, customer, cancellationToken);
        if (assessment is null) return new(true);
        var context = new ProactiveCustomerAiContext(customer.CustomerId,
            policy.CanRead("customerCode") ? customer.CustomerCode : customer.CustomerId,
            policy.CanRead("status") ? customer.Status : null,
            assessment.HealthBand, assessment.ChurnRisk, assessment.ReasonCode, assessment.AlgorithmVersion);
        persistence.AddReadAudit(new CustomerReadAuditRecord("readProactiveCustomerAiContext", current.Trusted.WorkspaceId,
            current.Trusted.MemberId, customer.CustomerId, requestContext.RequestId, requestContext.CorrelationId,
            customer.Version, clock.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
        return new(true, context);
    }
}
