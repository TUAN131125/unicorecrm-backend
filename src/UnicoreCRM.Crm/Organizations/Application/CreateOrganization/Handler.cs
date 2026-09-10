using UnicoreCRM.Crm.Organizations.Application.Common;
using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Crm.Organizations.Domain;

namespace UnicoreCRM.Crm.Organizations.Application.CreateOrganization;
internal sealed record Command(CreateOrganizationRequest Request, OrganizationCommandMetadata Metadata);
internal sealed class Handler(OrganizationAuthorization authorization, IOrganizationsPersistence persistence, TimeProvider timeProvider)
{
    internal async Task<OrganizationOperationResult<OrganizationMutationResponse>> HandleAsync(Command command, CancellationToken ct)
    {
        var meta = new OrganizationRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(meta, OrganizationCapabilities.Create, ct);
        if (!access.IsSuccess) return OrganizationOperationResult<OrganizationMutationResponse>.Failure(access.Error!);
        var validation = OrganizationMutationSupport.Validate(command.Request.DisplayName, command.Request.Status, true);
        if (validation is not null) return OrganizationOperationResult<OrganizationMutationResponse>.Failure(validation);
        var trusted = access.Value!.Trusted; var fingerprint = OrganizationMutationSupport.Fingerprint(command.Request);
        await using var tx = await persistence.BeginSerializableAsync(ct);
        var scope = OrganizationMutationSupport.ScopeKey(trusted, "createOrganization", "WORKSPACE", command.Metadata.IdempotencyKey);
        var prior = await persistence.FindIdempotencyAsync(scope, ct);
        if (prior is not null) return prior.Fingerprint == fingerprint ? OrganizationOperationResult<OrganizationMutationResponse>.Success(OrganizationMutationSupport.Replay(prior)) : OrganizationOperationResult<OrganizationMutationResponse>.Failure(OrganizationErrors.IdempotencyReused());
        var now=timeProvider.GetUtcNow(); var organization=new Organization(trusted.WorkspaceId, trusted.MemberId, command.Request.DisplayName!.Trim(), command.Request.Status ?? "prospect", OrganizationMutationSupport.Profile(command.Request), now);
        persistence.AddOrganization(organization);
        var response=OrganizationMutationSupport.Commit(persistence, organization, trusted, command.Metadata, "createOrganization", "ORGANIZATION_CREATED", "WORKSPACE", fingerprint, now);
        try { await persistence.SaveChangesAsync(ct); } catch (OrganizationsPersistenceConcurrencyException) { return OrganizationOperationResult<OrganizationMutationResponse>.Failure(OrganizationErrors.IdempotencyReused()); }
        await tx.CommitAsync(ct); return OrganizationOperationResult<OrganizationMutationResponse>.Success(response);
    }
}
