using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Products.Application.Common;
using UnicoreCRM.Sales.Products.Contracts;

namespace UnicoreCRM.Sales.Products.Application.ListProductConfigurationTypes;

internal sealed record Query(ProductRequestMetadata Metadata);

/// <summary>
/// Reads one Workspace's effective Product Configuration document.
///
/// <para>Authorization uses the plain capability boundary rather than the Product record-access
/// evaluator: this is a SYSTEM_CONFIGURATION resource with no record scope and no field security, so
/// applying the Products record policies to it would let a data-scope rule about Product records
/// decide configuration visibility, which nothing authorises.</para>
///
/// <para>The trusted Workspace comes from the AccessControl-resolved authorization context. Products
/// never reads the X-Workspace-Id header, and never queries Workspace or AccessControl
/// persistence.</para>
/// </summary>
internal sealed class Handler(
    IAccessAuthorizer authorizer,
    IProductsPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<ProductOperationResult<ConfigurationDocumentResponse>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var metadata = new ProductRequestMetadata(query.Metadata.RequestId, query.Metadata.CorrelationId);
        var decision = await authorizer.AuthorizeAsync(
            ProductConfigurationCapabilities.StudioRead,
            metadata.CorrelationId,
            cancellationToken);
        if (!decision.IsAllowed || decision.Context is not { } context)
        {
            return ProductOperationResult<ConfigurationDocumentResponse>.Failure(
                string.Equals(decision.Code, "WORKSPACE_MISMATCH", StringComparison.Ordinal)
                    ? ProductErrors.WorkspaceMismatch()
                    : ProductErrors.AccessDenied());
        }

        var trusted = new TrustedWorkspaceContext(
            context.WorkspaceId,
            context.AccountId,
            context.MemberId,
            context.MembershipId);

        // Anchor and overrides are read as one snapshot, so the revision the response reports and the
        // ETag derived from it always describe the document the response carries.
        var state = await persistence.ReadProductConfigurationAsync(trusted.WorkspaceId, cancellationToken);
        var projected = ProductConfigurationCatalog.Project(state);
        if (!projected.IsSuccess)
        {
            // Corrupt owner-owned state fails closed. No repair, no partial document and no success
            // evidence: the read never observed a document it could attest to.
            return ProductOperationResult<ConfigurationDocumentResponse>.Failure(projected.Error!);
        }

        // Success evidence is established before the response is returned, and it fails the request if
        // it cannot be committed. A 200 whose trusted-revision mark was never raised would leave the
        // next rollback below that revision undetectable.
        await ProductReadAudit.RecordConfigurationAsync(
            persistence,
            projected.Value!.Revision,
            trusted,
            metadata,
            "listProductConfigurationTypes",
            timeProvider.GetUtcNow(),
            cancellationToken);
        return projected;
    }
}
