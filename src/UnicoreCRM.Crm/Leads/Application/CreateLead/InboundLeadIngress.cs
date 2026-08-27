using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.CreateLead;

internal sealed class InboundLeadIngress(
    IDelegatedAccessAuthorizer accessAuthorizer,
    LeadCreateExecution execution) : IInboundLeadIngress
{
    public async Task<InboundLeadCreateResult> CreateAsync(
        InboundLeadCreateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!string.Equals(
                command.TrustedWorkspace.MemberId,
                command.Provenance.DelegatedSubjectId,
                StringComparison.Ordinal))
        {
            return Failure("ACCESS_DENIED", 403, null);
        }

        var decision = await accessAuthorizer.AuthorizeAsync(
            command.TrustedWorkspace,
            LeadCapabilities.Create,
            command.CorrelationId,
            cancellationToken);
        if (!decision.IsAllowed)
            return Failure(decision.Code, 403, null);

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
            command.TrustedWorkspace,
            null,
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
