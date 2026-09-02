using System.Text.Json.Serialization;
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

    /// <summary>
    /// The admitted mutation capability of the Product Configuration surface. Unlike the read, whose
    /// canonical metadata declares studio.read, the mutation is a products.* capability whose
    /// semantic owner is Products, exactly as the pinned admission row declares.
    /// </summary>
    public static AccessRequirement Configure { get; } = AccessRequirement.ForCanonicalCapability("products.configure");
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

/// <summary>
/// The admitted request body of updateProductConfigurationType. It carries exactly one field: the
/// effective status the caller wants the always-existing resource to have. Unmapped members are
/// rejected so the frozen <c>additionalProperties: false</c> schema is enforced on the wire rather
/// than silently ignored.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateProductConfigurationTypeRequest(string? Status);

/// <summary>
/// The command envelope of a Product Configuration mutation. It mirrors the Products mutation
/// envelope field-for-field and reuses <see cref="ConfigurationDocumentResponse"/> as its result, so
/// no second document shape exists.
///
/// <para><see cref="Version"/> is the Workspace Product Configuration <b>document</b> revision after
/// the command, never a per-override row version: the document is the versioned resource, and the
/// response ETag is this value.</para>
///
/// <para><see cref="EmittedEventIds"/> is always empty. No Product Configuration event contract
/// exists, so the outbox expectation is DEFERRED and no event type is invented.</para>
/// </summary>
public sealed record ProductConfigurationMutationResponse(
    string CommandId,
    string CorrelationId,
    string AggregateId,
    string AggregateType,
    long Version,
    string OccurredAt,
    string Outcome,
    ConfigurationDocumentResponse Result,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> EmittedEventIds,
    IReadOnlyList<string> AuditEvidenceIds);
