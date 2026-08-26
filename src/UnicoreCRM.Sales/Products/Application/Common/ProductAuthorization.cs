using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Sales.Products.Application.Common;

internal sealed class ProductAuthorization(
    ICurrentWorkspace currentWorkspace,
    IAccessAuthorizer accessAuthorizer)
{
    internal async Task<ProductOperationResult<TrustedWorkspaceContext>> AuthorizeAsync(
        AccessRequirement requirement,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!currentWorkspace.IsResolved)
            return ProductOperationResult<TrustedWorkspaceContext>.Failure(ProductErrors.WorkspaceMismatch());

        var decision = await accessAuthorizer.AuthorizeAsync(requirement, correlationId, cancellationToken);
        return decision.IsAllowed
            ? ProductOperationResult<TrustedWorkspaceContext>.Success(currentWorkspace.Require())
            : ProductOperationResult<TrustedWorkspaceContext>.Failure(
                decision.Code == "WORKSPACE_MISMATCH" ? ProductErrors.WorkspaceMismatch() : ProductErrors.AccessDenied());
    }
}
