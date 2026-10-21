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
        var validation = OrganizationMutationSupport.Validate(command.Request);
        if (validation is not null) return OrganizationOperationResult<OrganizationMutationResponse>.Failure(validation);
        var forbidden = OrganizationFieldSecurity.GuardCreateWrite(access.Value!.Authorization, command.Request);
        if (forbidden is not null) return OrganizationOperationResult<OrganizationMutationResponse>.Failure(forbidden);
        var trusted = access.Value!.Trusted; var fingerprint = OrganizationMutationSupport.Fingerprint(command.Request);
        await using var tx = await persistence.BeginSerializableAsync(ct);
        var scope = OrganizationMutationSupport.ScopeKey(trusted, "createOrganization", "WORKSPACE", command.Metadata.IdempotencyKey);
        var prior = await persistence.FindIdempotencyAsync(scope, ct);
        if (prior is not null)
        {
            if (prior.Fingerprint != fingerprint) return OrganizationOperationResult<OrganizationMutationResponse>.Failure(OrganizationErrors.IdempotencyReused());
            var replay = OrganizationMutationSupport.Replay(prior, access.Value.Authorization);
            var target = await persistence.ReadOrganizationAsync(trusted.WorkspaceId, replay.AggregateId, ct);
            if (target is null) return OrganizationOperationResult<OrganizationMutationResponse>.Failure(OrganizationErrors.NotFound());
            var denied = await authorization.EnforceRecordAsync(access.Value, target, "createOrganizationReplay", meta, ct);
            return denied is null ? OrganizationOperationResult<OrganizationMutationResponse>.Success(replay)
                : OrganizationOperationResult<OrganizationMutationResponse>.Failure(denied);
        }
        var now=timeProvider.GetUtcNow(); var organization=new Organization(trusted.WorkspaceId, trusted.MemberId, command.Request.DisplayName!.Trim(), command.Request.Status ?? "prospect", OrganizationMutationSupport.Profile(command.Request), now);
        persistence.AddOrganization(organization);
        var response=OrganizationMutationSupport.Commit(persistence, organization, access.Value, command.Metadata, "createOrganization", "ORGANIZATION_CREATED", "WORKSPACE", fingerprint, now);
        try { await persistence.SaveChangesAsync(ct); } catch (OrganizationsPersistenceConcurrencyException) { return OrganizationOperationResult<OrganizationMutationResponse>.Failure(OrganizationErrors.IdempotencyReused()); }
        await tx.CommitAsync(ct); return OrganizationOperationResult<OrganizationMutationResponse>.Success(response);
    }
}
