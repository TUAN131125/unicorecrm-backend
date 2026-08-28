using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Customers.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Customers.Application.Common;

internal sealed record CustomerAccess(TrustedWorkspaceContext Trusted, RecordAccessAuthorization Authorization);

internal static class CustomerFieldSecurity
{
    internal static IReadOnlyDictionary<string, bool> EnforceableFields { get; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = true,
            ["workspaceId"] = true,
            ["customerCode"] = true,
            ["type"] = true,
            ["relationshipRef"] = true,
            ["status"] = true,
            ["health"] = true,
            ["firstPurchaseAt"] = true,
            ["lastPurchaseAt"] = true,
            ["version"] = true,
            ["createdAt"] = true,
            ["updatedAt"] = true,
            ["calculatedHealth"] = false,
            ["manualHealthOverride"] = false,
            ["onboardingStatus"] = false,
            ["onboardingCompletedAt"] = false,
            ["createdFromEvidenceId"] = false,
            ["conversionPolicyVersion"] = false,
            ["conversionCorrelationId"] = false,
            ["sourceSystem"] = false,
            ["externalCustomerRef"] = false,
            ["tier"] = false,
            ["serviceLevel"] = false,
            ["careCadenceDays"] = false,
            ["careOwnerId"] = false,
            ["segment"] = false,
            ["tags"] = false,
            ["nextCareAt"] = false,
            ["lastCareAt"] = false
        };

    internal static IReadOnlyList<string> FieldKeys { get; } =
        EnforceableFields.Keys.Order(StringComparer.Ordinal).ToArray();

    internal static CustomerDocument Project(CustomerDocument model, RecordAccessAuthorization access) =>
        model with
        {
            CalculatedHealth = Keep(access, "calculatedHealth", model.CalculatedHealth),
            ManualHealthOverride = Keep(access, "manualHealthOverride", model.ManualHealthOverride),
            OnboardingStatus = Keep(access, "onboardingStatus", model.OnboardingStatus),
            OnboardingCompletedAt = Keep(access, "onboardingCompletedAt", model.OnboardingCompletedAt),
            CreatedFromEvidenceId = Keep(access, "createdFromEvidenceId", model.CreatedFromEvidenceId),
            ConversionPolicyVersion = Keep(access, "conversionPolicyVersion", model.ConversionPolicyVersion),
            ConversionCorrelationId = Keep(access, "conversionCorrelationId", model.ConversionCorrelationId),
            SourceSystem = Keep(access, "sourceSystem", model.SourceSystem),
            ExternalCustomerRef = Keep(access, "externalCustomerRef", model.ExternalCustomerRef),
            Tier = Keep(access, "tier", model.Tier),
            ServiceLevel = Keep(access, "serviceLevel", model.ServiceLevel),
            CareCadenceDays = Keep(access, "careCadenceDays", model.CareCadenceDays),
            CareOwnerId = Keep(access, "careOwnerId", model.CareOwnerId),
            Segment = Keep(access, "segment", model.Segment),
            Tags = access.CanRead("tags") ? model.Tags : null,
            NextCareAt = Keep(access, "nextCareAt", model.NextCareAt),
            LastCareAt = Keep(access, "lastCareAt", model.LastCareAt)
        };

    internal static CustomerOperationError? UnenforceablePolicy(RecordAccessAuthorization access) =>
        access.UnenforceableFieldKeys.Count == 0
            ? null
            : new CustomerOperationError(
                "ACCESS_DENIED",
                403,
                "Access denied",
                "A field-security policy applies to a required Customer field, so the request is refused rather than returning a value the policy forbids.");

    private static string? Keep(RecordAccessAuthorization access, string fieldKey, string? value) =>
        access.CanRead(fieldKey) ? value : null;

    private static int? Keep(RecordAccessAuthorization access, string fieldKey, int? value) =>
        access.CanRead(fieldKey) ? value : null;
}

internal sealed class CustomerAuthorization(IRecordAccessEvaluator evaluator)
{
    internal const string ResourceKey = "customers";

    internal async Task<CustomerOperationResult<CustomerAccess>> AuthorizeAsync(
        CustomerRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var authorization = await evaluator.AuthorizeResourceAsync(
            ResourceKey,
            CustomerCapabilities.View.Capability,
            CustomerFieldSecurity.FieldKeys,
            RecordAccessRepresentation.Full,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);

        if (authorization.TrustedWorkspace is not { } trusted)
        {
            return CustomerOperationResult<CustomerAccess>.Failure(
                authorization.Code == "WORKSPACE_MISMATCH"
                    ? CustomerErrors.WorkspaceMismatch()
                    : CustomerErrors.AccessDenied());
        }
        if (!authorization.IsAllowed)
            return CustomerOperationResult<CustomerAccess>.Failure(CustomerErrors.AccessDenied());

        var unenforceable = CustomerFieldSecurity.UnenforceablePolicy(authorization);
        return unenforceable is null
            ? CustomerOperationResult<CustomerAccess>.Success(new CustomerAccess(trusted, authorization))
            : CustomerOperationResult<CustomerAccess>.Failure(unenforceable);
    }

    internal async Task<CustomerOperationError?> EnforceRecordAsync(
        CustomerAccess access,
        Customer customer,
        string enforcementPoint,
        CustomerRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var decision = await evaluator.AuthorizeRecordAsync(
            access.Authorization,
            customer.CustomerId,
            Facts(customer),
            enforcementPoint,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);
        return decision.IsAllowed ? null : CustomerErrors.NotFound();
    }

    // careOwnerId is a Customer document field, not a proven canonical AccessControl owner fact.
    // Relationship targets are likewise not inherited ownership. OWN therefore fails closed.
    internal static RecordAccessFacts Facts(Customer customer) => RecordAccessFacts.Found(null);
}
