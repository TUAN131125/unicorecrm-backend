using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Crm.Organizations.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Organizations.Application.Common;

internal static class OrganizationMutationSupport
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static string Fingerprint<T>(T value) => Hash(JsonSerializer.Serialize(value, Json));
    internal static string ScopeKey(TrustedWorkspaceContext trusted, string operation, string target, string key) => Hash($"{trusted.WorkspaceId}\n{operation}\n{trusted.MemberId}\n{target}\n{key}");
    internal static OrganizationMutationResponse Replay(OrganizationIdempotencyRecord record) =>
        (JsonSerializer.Deserialize<OrganizationMutationResponse>(record.ResponseJson, Json) ?? throw new InvalidOperationException("Invalid Organization replay record.")) with { Outcome = "REPLAYED" };
    internal static OrganizationMutationResponse Commit(IOrganizationsPersistence persistence, Organization organization,
        TrustedWorkspaceContext trusted, OrganizationCommandMetadata metadata, string operation, string eventType,
        string target, string fingerprint, DateTimeOffset now)
    {
        var audit = new OrganizationAuditRecord(operation, trusted.WorkspaceId, trusted.MemberId, organization.OrganizationId, metadata.RequestId, metadata.CorrelationId, organization.Version, now);
        var message = new OrganizationOutboxMessage(eventType, organization.OrganizationId, trusted.WorkspaceId, metadata.CorrelationId,
            JsonSerializer.Serialize(new { organizationId = organization.OrganizationId, resourceVersion = organization.Version }, Json), now);
        var response = new OrganizationMutationResponse($"command_{Guid.NewGuid():N}", metadata.CorrelationId,
            organization.OrganizationId, "ORGANIZATION", organization.Version, OrganizationProjection.TimestampValue(now),
            "COMMITTED", OrganizationProjection.Document(organization), [], [message.EventId], [audit.AuditId]);
        persistence.AddAudit(audit); persistence.AddOutbox(message);
        persistence.AddIdempotency(new OrganizationIdempotencyRecord(ScopeKey(trusted, operation, target, metadata.IdempotencyKey), trusted.WorkspaceId, operation, trusted.MemberId, target, metadata.IdempotencyKey, fingerprint, JsonSerializer.Serialize(response, Json), now));
        return response;
    }
    internal static OrganizationProfile Profile(CreateOrganizationRequest r) => new() { LegalName=Trim(r.LegalName), TaxCode=Trim(r.TaxCode), Domain=Trim(r.Domain), Website=r.Website, Industry=Trim(r.Industry), SizeBand=Trim(r.SizeBand), EmployeeCount=r.EmployeeCount, AnnualRevenue=r.AnnualRevenue, Email=r.Email, Phone=r.Phone, Address=r.Address, AddressDetails=Address(r.AddressDetails), Source=r.Source, RelationshipLevel=r.RelationshipLevel, Notes=r.Notes };
    internal static OrganizationProfile Merge(OrganizationProfile p, UpdateOrganizationRequest r) => p with { LegalName=r.LegalName is null ? p.LegalName : Trim(r.LegalName), TaxCode=r.TaxCode is null ? p.TaxCode : Trim(r.TaxCode), Domain=r.Domain is null ? p.Domain : Trim(r.Domain), Website=r.Website ?? p.Website, Industry=r.Industry is null ? p.Industry : Trim(r.Industry), SizeBand=r.SizeBand is null ? p.SizeBand : Trim(r.SizeBand), EmployeeCount=r.EmployeeCount ?? p.EmployeeCount, AnnualRevenue=r.AnnualRevenue ?? p.AnnualRevenue, Email=r.Email ?? p.Email, Phone=r.Phone ?? p.Phone, Address=r.Address ?? p.Address, AddressDetails=r.AddressDetails is null ? p.AddressDetails : Address(r.AddressDetails), Source=r.Source ?? p.Source, RelationshipLevel=r.RelationshipLevel ?? p.RelationshipLevel, Notes=r.Notes ?? p.Notes };
    internal static OrganizationPostalAddress? Address(OrganizationPostalAddressDocument? a) => a is null ? null : new(a.Line1) { Line2=a.Line2, Ward=a.Ward, District=a.District, Province=a.Province, Country=a.Country, PostalCode=a.PostalCode, Formatted=a.Formatted };
    internal static OrganizationOperationError? Validate(CreateOrganizationRequest request)
        => Validate(request.DisplayName, request.Status, true, request.LegalName, request.TaxCode,
            request.Domain, request.Industry, request.SizeBand);
    internal static OrganizationOperationError? Validate(UpdateOrganizationRequest request)
        => Validate(request.DisplayName, request.Status, false, request.LegalName, request.TaxCode,
            request.Domain, request.Industry, request.SizeBand);
    private static OrganizationOperationError? Validate(string? displayName, string? status, bool requireName,
        string? legalName, string? taxCode, string? domain, string? industry, string? sizeBand)
    {
        var fields = new Dictionary<string,string[]>();
        if (requireName && string.IsNullOrWhiteSpace(displayName)) fields["displayName"]=["displayName is required."];
        if (displayName?.Trim().Length > 200) fields["displayName"]=["displayName must not exceed 200 characters."];
        AddLengthError(fields, "legalName", legalName, 240);
        AddLengthError(fields, "taxCode", taxCode, 80);
        AddLengthError(fields, "domain", domain, 200);
        AddLengthError(fields, "industry", industry, 160);
        AddLengthError(fields, "sizeBand", sizeBand, 80);
        if (status is not null && status is not ("prospect" or "active" or "strategic" or "inactive")) fields["status"]=["status is invalid."];
        return fields.Count == 0 ? null : OrganizationErrors.Validation(fields);
    }
    private static void AddLengthError(IDictionary<string, string[]> fields, string field, string? value, int maxLength)
    {
        if (value?.Trim().Length > maxLength) fields[field] = [$"{field} must not exceed {maxLength} characters."];
    }
    private static string? Trim(string? value) => value?.Trim();
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
