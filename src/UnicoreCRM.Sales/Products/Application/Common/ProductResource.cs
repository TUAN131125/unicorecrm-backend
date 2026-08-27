using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Products.Domain;

namespace UnicoreCRM.Sales.Products.Application.Common;

internal static class ProductResource
{
    /// <summary>
    /// Resolves a Workspace-scoped lookup into a result. It no longer decides Workspace ownership:
    /// the lookup itself constrains the trusted Workspace in SQL, so a foreign Product is never
    /// materialised and arrives here as <c>null</c>, indistinguishable from an unknown one.
    ///
    /// <para>The previous shape loaded a Product by global identifier and then compared its
    /// Workspace, answering <c>RESOURCE_NOT_FOUND</c> for an unknown identifier and
    /// <c>WORKSPACE_MISMATCH</c> for a real Product of another Workspace. That difference is an
    /// existence oracle: a caller who could guess an identifier could tell a real foreign Product
    /// from a non-existent one. Both now collapse to not found.</para>
    /// </summary>
    internal static ProductOperationResult<Product> Resolve(Product? product) =>
        product is null
            ? ProductOperationResult<Product>.Failure(ProductErrors.NotFound())
            : ProductOperationResult<Product>.Success(product);
}
