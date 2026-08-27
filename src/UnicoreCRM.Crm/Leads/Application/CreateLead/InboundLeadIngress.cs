using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.CreateLead;

internal sealed class InboundLeadIngress(
    IDelegatedLeadCreateAuthorizer authorization,
    LeadCreateExecution execution) : IInboundLeadIngress
{
    public async Task<InboundLeadCreateResult> CreateAsync(
        InboundLeadCreateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var delegatedSubjectId = command.Provenance.DelegatedSubjectId;
        if (delegatedSubjectId is null)
            return Failure("ACCESS_DENIED", 403, null);

        var authorizationResult = await authorization.AuthorizeAsync(
            command.TrustedWorkspace,
            delegatedSubjectId,
            command.CorrelationId,
            cancellationToken);
        if (authorizationResult.Authorization is null)
            return Failure(authorizationResult.Code, 403, null);

        var metadata = new LeadCommandMetadata(
            command.RequestId,
            command.CorrelationId,
            command.IdempotencyKey,
            null,
            command.Provenance.ActorType,
            command.Provenance.ActorId,
            command.Provenance.DelegatedSubjectId,
            command.Provenance.SourceReference);
        var result = await execution.ExecuteAsync(
            LeadCreateAdmission.DelegatedIngress(authorizationResult.Authorization),
            command.Request,
            metadata,
            cancellationToken);
        return result.IsSuccess
            ? new InboundLeadCreateResult(
                true,
                result.Value!.AggregateId,
                result.Value.Outcome,
                null,
                null,
                null)
            : Failure(result.Error!.Code, result.Error.Status, result.Error.FieldErrors);
    }

    private static InboundLeadCreateResult Failure(
        string code,
        int status,
        IReadOnlyDictionary<string, string[]>? fieldErrors) =>
        new(false, null, null, code, status, fieldErrors);
}
