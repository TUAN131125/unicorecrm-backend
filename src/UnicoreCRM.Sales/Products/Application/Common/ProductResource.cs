using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Products.Domain;

namespace UnicoreCRM.Sales.Products.Application.Common;

internal static class ProductResource
{
    internal static ProductOperationResult<Product> ValidateOwned(
        Product? product,
        TrustedWorkspaceContext trusted)
    {
        if (product is null)
            return ProductOperationResult<Product>.Failure(ProductErrors.NotFound());
        return string.Equals(product.WorkspaceId, trusted.WorkspaceId, StringComparison.Ordinal)
            ? ProductOperationResult<Product>.Success(product)
            : ProductOperationResult<Product>.Failure(ProductErrors.WorkspaceMismatch());
    }
}
