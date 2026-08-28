using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Crm.Organizations.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Organizations.Application.Common;

internal sealed record OrganizationAccess(TrustedWorkspaceContext Trusted, RecordAccessAuthorization Authorization);

internal static class OrganizationFieldSecurity
{
    internal static IReadOnlyDictionary<string, bool> EnforceableFields { get; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = true,
            ["workspaceId"] = true,
            ["displayName"] = true,
            ["status"] = true,
            ["version"] = true,
            ["createdAt"] = true,
            ["updatedAt"] = true,
            ["legalName"] = false,
            ["taxCode"] = false,
            ["domain"] = false,
            ["website"] = false,
            ["industry"] = false,
            ["sizeBand"] = false,
            ["employeeCount"] = false,
            ["annualRevenue"] = false,
            ["email"] = false,
            ["phone"] = false,
            ["address"] = false,
            ["addressDetails"] = false,
            ["source"] = false,
            ["ownerId"] = false,
            ["primaryContactId"] = false,
            ["contactRefs"] = false,
            ["relationshipLevel"] = false,
            ["notes"] = false,
            ["externalRef"] = false
        };

    internal static IReadOnlyList<string> FieldKeys { get; } =
        EnforceableFields.Keys.Order(StringComparer.Ordinal).ToArray();

    internal static OrganizationDocument Project(OrganizationDocument model, RecordAccessAuthorization access) =>
        model with
        {
            LegalName = Keep(access, "legalName", model.LegalName),
            TaxCode = Keep(access, "taxCode", model.TaxCode),
            Domain = Keep(access, "domain", model.Domain),
            Website = Keep(access, "website", model.Website),
            Industry = Keep(access, "industry", model.Industry),
            SizeBand = Keep(access, "sizeBand", model.SizeBand),
            EmployeeCount = Keep(access, "employeeCount", model.EmployeeCount),
            AnnualRevenue = Keep(access, "annualRevenue", model.AnnualRevenue),
            Email = Keep(access, "email", model.Email),
            Phone = Keep(access, "phone", model.Phone),
            Address = Keep(access, "address", model.Address),
            AddressDetails = access.CanRead("addressDetails") ? model.AddressDetails : null,
            Source = Keep(access, "source", model.Source),
            OwnerId = Keep(access, "ownerId", model.OwnerId),
            PrimaryContactId = Keep(access, "primaryContactId", model.PrimaryContactId),
            ContactRefs = access.CanRead("contactRefs") ? model.ContactRefs : null,
            RelationshipLevel = Keep(access, "relationshipLevel", model.RelationshipLevel),
            Notes = Keep(access, "notes", model.Notes),
            ExternalRef = Keep(access, "externalRef", model.ExternalRef)
        };

    internal static OrganizationOperationError? UnenforceablePolicy(RecordAccessAuthorization access) =>
        access.UnenforceableFieldKeys.Count == 0
            ? null
            : new OrganizationOperationError(
                "ACCESS_DENIED",
                403,
                "Access denied",
                "A field-security policy applies to a required Organization field, so the request is refused rather than returning a value the policy forbids.");

    private static string? Keep(RecordAccessAuthorization access, string fieldKey, string? value) =>
        access.CanRead(fieldKey) ? value : null;

    private static int? Keep(RecordAccessAuthorization access, string fieldKey, int? value) =>
        access.CanRead(fieldKey) ? value : null;

    private static decimal? Keep(RecordAccessAuthorization access, string fieldKey, decimal? value) =>
        access.CanRead(fieldKey) ? value : null;
}

internal sealed class OrganizationAuthorization(IRecordAccessEvaluator evaluator)
{
    internal const string ResourceKey = "organizations";

    internal async Task<OrganizationOperationResult<OrganizationAccess>> AuthorizeAsync(
        OrganizationRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var authorization = await evaluator.AuthorizeResourceAsync(
            ResourceKey,
            OrganizationCapabilities.Read.Capability,
            OrganizationFieldSecurity.FieldKeys,
            RecordAccessRepresentation.Full,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);

        if (authorization.TrustedWorkspace is not { } trusted)
        {
            return OrganizationOperationResult<OrganizationAccess>.Failure(
                authorization.Code == "WORKSPACE_MISMATCH"
                    ? OrganizationErrors.WorkspaceMismatch()
                    : OrganizationErrors.AccessDenied());
        }
        if (!authorization.IsAllowed)
            return OrganizationOperationResult<OrganizationAccess>.Failure(OrganizationErrors.AccessDenied());

        var unenforceable = OrganizationFieldSecurity.UnenforceablePolicy(authorization);
        return unenforceable is null
            ? OrganizationOperationResult<OrganizationAccess>.Success(new OrganizationAccess(trusted, authorization))
            : OrganizationOperationResult<OrganizationAccess>.Failure(unenforceable);
    }

    internal async Task<OrganizationOperationError?> EnforceRecordAsync(
        OrganizationAccess access,
        Organization organization,
        string enforcementPoint,
        OrganizationRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var decision = await evaluator.AuthorizeRecordAsync(
            access.Authorization,
            organization.OrganizationId,
            Facts(organization),
            enforcementPoint,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);
        return decision.IsAllowed ? null : OrganizationErrors.NotFound();
    }

    // Current authority admits ownerId as an Organization field, but does not establish it as the
    // canonical AccessControl ownership fact. Returning no owner makes OWN fail closed.
    internal static RecordAccessFacts Facts(Organization organization) => RecordAccessFacts.Found(null);
}
