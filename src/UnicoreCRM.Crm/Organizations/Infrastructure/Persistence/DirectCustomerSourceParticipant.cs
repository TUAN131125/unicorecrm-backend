using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Organizations.Application.Common;
using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Organizations.Infrastructure.Persistence;

internal sealed class DirectCustomerSourceParticipant(OrganizationsDbContext db, OrganizationAuthorization authorization,
    Application.CreateOrganization.Handler create) : IDirectCustomerSourceParticipant
{
    public string BuyerType => "ORGANIZATION_ACCOUNT";
    public async Task<IReadOnlyList<CustomerPhoneCandidate>> FindVisiblePhoneMatchesAsync(OwnerTransaction transaction,
        string normalizedPhone, string requestId, string correlationId, CancellationToken ct)
    {
        var metadata = new OrganizationRequestMetadata(requestId, correlationId);
        var grant = await authorization.AuthorizeAsync(metadata, ct);
        if (!grant.IsSuccess || !grant.Value!.Authorization.CanRead("phone") || !grant.Value.Authorization.CanRead("displayName")) return [];
        var access = grant.Value;
        if (access.Authorization.ScopeFilter == RecordAccessScopeFilter.Denied) return [];
        db.Database.SetDbConnection(transaction.Connection, false);
        await db.Database.UseTransactionAsync(transaction.Transaction, ct);
        var forms = CustomerPhoneComparison.ExactStoredForms(normalizedPhone);
        var query = db.Organizations.FromSqlRaw("SELECT * FROM organizations.Organizations WHERE WorkspaceId=@ws AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(JSON_VALUE([Profile],'$.phone'),' ',''),CHAR(9),''),'-',''),'(',''),')',''),'.','') IN (@p0,@p1,@p2)",
            new SqlParameter("@ws", access.Trusted.WorkspaceId), new SqlParameter("@p0", forms[0]),
            new SqlParameter("@p1", forms.Count > 1 ? forms[1] : forms[0]), new SqlParameter("@p2", forms.Count > 2 ? forms[2] : forms[0])).AsNoTracking();
        if (access.Authorization.ScopeFilter == RecordAccessScopeFilter.OwnedByMember)
        {
            if (access.Authorization.ScopeOwnerMemberId is not { } owner) return [];
            query = query.Where(o => o.OwnerId == owner);
        }
        var matches = new List<CustomerPhoneCandidate>();
        await foreach (var organization in query.AsAsyncEnumerable().WithCancellation(ct))
        {
            if (await authorization.EnforceRecordAsync(access, organization, "checkDirectCustomerPhone", metadata, ct) is not null) continue;
            if (CustomerPhoneComparison.Normalize(OrganizationProjection.Document(organization).Phone) != normalizedPhone) continue;
            matches.Add(new(new(BuyerType, organization.OrganizationId), organization.DisplayName, organization.Status == "archived"));
            if (matches.Count > 100) throw new BusinessCommandException("CUSTOMER_PHONE_MATCH_LIMIT", 422);
        }
        return matches;
    }

    public async Task<string> CreateAsync(OwnerTransaction transaction, CreateDirectCustomerRequest request, string key,
        string requestId, string correlationId, CancellationToken ct)
    {
        var result = await create.HandleAsync(new(new CreateOrganizationRequest(DisplayName: request.Name,
            LegalName: request.LegalName, TaxCode: request.TaxCode, Email: request.Email, Phone: request.Phone,
            Address: request.Address, Notes: request.Notes, Source: "direct_customer"), new(requestId, correlationId, key, null)), ct, transaction);
        if (!result.IsSuccess)
        {
            if (result.Error!.FieldErrors is { } errors) throw new CustomerSourceValidationException(errors.ToDictionary(
                e => e.Key == "displayName" ? "name" : e.Key, e => e.Value));
            throw new BusinessCommandException(result.Error.Code, result.Error.Status);
        }
        return result.Value!.AggregateId;
    }
}
