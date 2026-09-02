using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Sales.Products.Contracts;

public static class ProductConfigurationCapabilities
{
    /// <summary>
    /// The admitted read capability of the Product Configuration surface. It is deliberately not a
    /// products.* capability: the canonical operation metadata declares studio.read, whose semantic
    /// owner is Studio. That is an authorization vocabulary statement only and confers no Studio
    /// ownership of this state, which stays Products-owned and Products-persisted.
    /// </summary>
    public static AccessRequirement StudioRead { get; } = AccessRequirement.ForCanonicalCapability("studio.read");
}

/// <summary>One canonical ProductType and its effective Workspace eligibility status.</summary>
public sealed record ProductConfigurationTypeEntry(string Code, string Status);

public sealed record ProductConfigurationData(IReadOnlyList<ProductConfigurationTypeEntry> Types);

/// <summary>
/// The admitted business representation carried by the contract's ConfigurationDocumentResponse.
/// The outer envelope is unchanged; <c>data</c> is the exact frozen shape and carries no additional
/// business properties - no row identifier, no timestamps, no labels and no behaviour flags.
/// </summary>
public sealed record ConfigurationDocumentResponse(long Revision, ProductConfigurationData Data);
