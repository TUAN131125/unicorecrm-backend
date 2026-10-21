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

    internal static IReadOnlyList<string> UpdateFieldKeys { get; } = ["displayName", "status", "legalName", "taxCode", "domain", "website", "industry", "sizeBand", "employeeCount", "annualRevenue", "email", "phone", "address", "addressDetails", "source", "relationshipLevel", "notes"];

    internal static OrganizationOperationError? GuardCreateWrite(RecordAccessAuthorization access, CreateOrganizationRequest request)
    {
        var fields = new List<string> { "displayName", "status" };
        if (request.LegalName is not null) fields.Add("legalName");
        if (request.TaxCode is not null) fields.Add("taxCode");
        if (request.Domain is not null) fields.Add("domain");
        if (request.Website is not null) fields.Add("website");
        if (request.Industry is not null) fields.Add("industry");
        if (request.SizeBand is not null) fields.Add("sizeBand");
        if (request.EmployeeCount is not null) fields.Add("employeeCount");
        if (request.AnnualRevenue is not null) fields.Add("annualRevenue");
        if (request.Email is not null) fields.Add("email");
        if (request.Phone is not null) fields.Add("phone");
        if (request.Address is not null) fields.Add("address");
        if (request.AddressDetails is not null) fields.Add("addressDetails");
        if (request.Source is not null) fields.Add("source");
        if (request.RelationshipLevel is not null) fields.Add("relationshipLevel");
        if (request.Notes is not null) fields.Add("notes");
        return GuardWrite(access, fields);
    }

    internal static OrganizationOperationError? GuardUpdateWrite(RecordAccessAuthorization access,
        Organization current, UpdateOrganizationRequest request)
    {
        // PATCH null/omission preserves the stored value. Compare the same normalized
        // candidate the domain will persist, rather than treating every DTO member as a write.
        var candidate = OrganizationMutationSupport.Merge(current.Profile, request);
        var fields = new List<string>();
        void Changed<T>(string key, T previous, T next)
        {
            if (!EqualityComparer<T>.Default.Equals(previous, next)) fields.Add(key);
        }
        Changed("displayName", current.DisplayName, request.DisplayName?.Trim() ?? current.DisplayName);
        Changed("status", current.Status, request.Status ?? current.Status);
        Changed("legalName", current.Profile.LegalName, candidate.LegalName);
        Changed("taxCode", current.Profile.TaxCode, candidate.TaxCode);
        Changed("domain", current.Profile.Domain, candidate.Domain);
        Changed("website", current.Profile.Website, candidate.Website);
        Changed("industry", current.Profile.Industry, candidate.Industry);
        Changed("sizeBand", current.Profile.SizeBand, candidate.SizeBand);
        Changed("employeeCount", current.Profile.EmployeeCount, candidate.EmployeeCount);
        Changed("annualRevenue", current.Profile.AnnualRevenue, candidate.AnnualRevenue);
        Changed("email", current.Profile.Email, candidate.Email);
        Changed("phone", current.Profile.Phone, candidate.Phone);
        Changed("address", current.Profile.Address, candidate.Address);
        Changed("addressDetails", current.Profile.AddressDetails, candidate.AddressDetails);
        Changed("source", current.Profile.Source, candidate.Source);
        Changed("relationshipLevel", current.Profile.RelationshipLevel, candidate.RelationshipLevel);
        Changed("notes", current.Profile.Notes, candidate.Notes);
        // A supplied withheld value is refused even if guessed correctly; otherwise
        // equality vs denial would itself reveal the hidden value.
        if (request.DisplayName is not null && !access.CanRead("displayName")) fields.Add("displayName");
        if (request.Status is not null && !access.CanRead("status")) fields.Add("status");
        if (request.LegalName is not null && !access.CanRead("legalName")) fields.Add("legalName");
        if (request.TaxCode is not null && !access.CanRead("taxCode")) fields.Add("taxCode");
        if (request.Domain is not null && !access.CanRead("domain")) fields.Add("domain");
        if (request.Website is not null && !access.CanRead("website")) fields.Add("website");
        if (request.Industry is not null && !access.CanRead("industry")) fields.Add("industry");
        if (request.SizeBand is not null && !access.CanRead("sizeBand")) fields.Add("sizeBand");
        if (request.EmployeeCount is not null && !access.CanRead("employeeCount")) fields.Add("employeeCount");
        if (request.AnnualRevenue is not null && !access.CanRead("annualRevenue")) fields.Add("annualRevenue");
        if (request.Email is not null && !access.CanRead("email")) fields.Add("email");
        if (request.Phone is not null && !access.CanRead("phone")) fields.Add("phone");
        if (request.Address is not null && !access.CanRead("address")) fields.Add("address");
        if (request.AddressDetails is not null && !access.CanRead("addressDetails")) fields.Add("addressDetails");
        if (request.Source is not null && !access.CanRead("source")) fields.Add("source");
        if (request.RelationshipLevel is not null && !access.CanRead("relationshipLevel")) fields.Add("relationshipLevel");
        if (request.Notes is not null && !access.CanRead("notes")) fields.Add("notes");
        return GuardWrite(access, fields);
    }

    internal static OrganizationOperationError? GuardWrite(RecordAccessAuthorization access, IEnumerable<string> fields)
    {
        return fields.Any(field => !access.CanWrite(field)) ? OrganizationErrors.AccessDenied() : null;
    }

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
        CancellationToken cancellationToken) => await AuthorizeAsync(metadata, OrganizationCapabilities.Read, cancellationToken);

    internal async Task<OrganizationOperationResult<OrganizationAccess>> AuthorizeAsync(
        OrganizationRequestMetadata metadata,
        AccessRequirement requirement,
        CancellationToken cancellationToken,
        RecordAccessRepresentation? representation = null)
    {
        var authorization = await evaluator.AuthorizeResourceAsync(
            ResourceKey,
            requirement.Capability,
            OrganizationFieldSecurity.FieldKeys,
            representation ?? RecordAccessRepresentation.Full,
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

    internal static RecordAccessFacts Facts(Organization organization) => RecordAccessFacts.Found(organization.OwnerId);
}
