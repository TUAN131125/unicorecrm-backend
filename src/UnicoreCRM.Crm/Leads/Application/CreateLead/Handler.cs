using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.CreateLead;

internal sealed record Command(CreateLeadRequest Request, LeadCommandMetadata Metadata);

internal sealed class Handler(
    LeadAuthorization authorization,
    LeadCreateExecution execution)
{
    internal async Task<LeadOperationResult<LeadMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(LeadCapabilities.Create, command.Metadata.CorrelationId, cancellationToken);
        if (!access.IsSuccess)
            return LeadOperationResult<LeadMutationResponse>.Failure(access.Error!);
        var trusted = access.Value!;
        return await execution.ExecuteAsync(trusted, command.Request, command.Metadata, cancellationToken);
    }
}
