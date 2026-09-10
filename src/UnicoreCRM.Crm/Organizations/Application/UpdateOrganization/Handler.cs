using UnicoreCRM.Crm.Organizations.Application.Common;
using UnicoreCRM.Crm.Organizations.Contracts;

namespace UnicoreCRM.Crm.Organizations.Application.UpdateOrganization;
internal sealed record Command(string OrganizationId, UpdateOrganizationRequest Request, OrganizationCommandMetadata Metadata);
internal sealed class Handler(OrganizationAuthorization authorization, IOrganizationsPersistence persistence, TimeProvider timeProvider)
{
    internal async Task<OrganizationOperationResult<OrganizationMutationResponse>> HandleAsync(Command command, CancellationToken ct)
    {
        var meta=new OrganizationRequestMetadata(command.Metadata.RequestId,command.Metadata.CorrelationId); var access=await authorization.AuthorizeAsync(meta,OrganizationCapabilities.Update,ct);
        if(!access.IsSuccess)return OrganizationOperationResult<OrganizationMutationResponse>.Failure(access.Error!);
        var validation=OrganizationMutationSupport.Validate(command.Request.DisplayName,command.Request.Status,false); if(validation is not null)return OrganizationOperationResult<OrganizationMutationResponse>.Failure(validation);
        var trusted=access.Value!.Trusted; var fingerprint=OrganizationMutationSupport.Fingerprint(new{command.OrganizationId,command.Request,command.Metadata.ExpectedVersion}); await using var tx=await persistence.BeginSerializableAsync(ct);
        var scope=OrganizationMutationSupport.ScopeKey(trusted,"updateOrganization",command.OrganizationId,command.Metadata.IdempotencyKey); var prior=await persistence.FindIdempotencyAsync(scope,ct);
        if(prior is not null)return prior.Fingerprint==fingerprint?OrganizationOperationResult<OrganizationMutationResponse>.Success(OrganizationMutationSupport.Replay(prior)):OrganizationOperationResult<OrganizationMutationResponse>.Failure(OrganizationErrors.IdempotencyReused());
        var organization=await persistence.LoadOrganizationAsync(trusted.WorkspaceId,command.OrganizationId,ct); if(organization is null)return OrganizationOperationResult<OrganizationMutationResponse>.Failure(OrganizationErrors.NotFound());
        var denied=await authorization.EnforceRecordAsync(access.Value,organization,"updateOrganization",meta,ct); if(denied is not null)return OrganizationOperationResult<OrganizationMutationResponse>.Failure(denied);
        if(organization.Status=="archived")return OrganizationOperationResult<OrganizationMutationResponse>.Failure(OrganizationErrors.AlreadyArchived());
        if(organization.Version!=command.Metadata.ExpectedVersion)return OrganizationOperationResult<OrganizationMutationResponse>.Failure(OrganizationErrors.VersionConflict(organization.OrganizationId,command.Metadata.ExpectedVersion!.Value,organization.Version));
        var now=timeProvider.GetUtcNow(); organization.Update(command.Request.DisplayName?.Trim()??organization.DisplayName,command.Request.Status??organization.Status,OrganizationMutationSupport.Merge(organization.Profile,command.Request),now);
        var response=OrganizationMutationSupport.Commit(persistence,organization,trusted,command.Metadata,"updateOrganization","ORGANIZATION_UPDATED",command.OrganizationId,fingerprint,now);
        try{await persistence.SaveChangesAsync(ct);}catch(OrganizationsPersistenceConcurrencyException){return OrganizationOperationResult<OrganizationMutationResponse>.Failure(OrganizationErrors.VersionConflict(organization.OrganizationId,command.Metadata.ExpectedVersion!.Value,organization.Version));} await tx.CommitAsync(ct); return OrganizationOperationResult<OrganizationMutationResponse>.Success(response);
    }
}
