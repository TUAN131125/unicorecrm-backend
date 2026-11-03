using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Contacts.Infrastructure.Persistence;

internal sealed class DirectCustomerSourceParticipant(ContactsDbContext db, ContactAuthorization authorization,
    Application.CreateContact.Handler create) : IDirectCustomerSourceParticipant
{
    public string BuyerType => "CONTACT";
    public async Task<IReadOnlyList<CustomerPhoneCandidate>> FindVisiblePhoneMatchesAsync(OwnerTransaction transaction,
        string normalizedPhone, string requestId, string correlationId, CancellationToken ct)
    {
        var metadata = new ContactRequestMetadata(requestId, correlationId);
        var grant = await authorization.AuthorizeAsync(metadata, ct);
        if (!grant.IsSuccess || !grant.Value!.Authorization.CanRead("fullName")) return [];
        var access = grant.Value;
        if (access.Authorization.ScopeFilter == RecordAccessScopeFilter.Denied) return [];
        var fields = new[] { "mobilePhone", "workPhone", "otherPhone" }.Where(access.Authorization.CanRead).ToArray();
        if (fields.Length == 0) return [];
        db.Database.SetDbConnection(transaction.Connection, false);
        await db.Database.UseTransactionAsync(transaction.Transaction, ct);
        var forms = CustomerPhoneComparison.ExactStoredForms(normalizedPhone);
        // Withheld fields contribute NULL, never a match. SQL text and paths are fixed.
        var query = db.Contacts.FromSqlRaw("""
            SELECT * FROM contacts.Contacts WHERE WorkspaceId=@ws AND (
            REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(CASE WHEN @mobile=1 THEN JSON_VALUE([Profile],'$.mobilePhone') END,' ',''),CHAR(9),''),'-',''),'(',''),')',''),'.','') IN (@p0,@p1,@p2)
            OR REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(CASE WHEN @work=1 THEN JSON_VALUE([Profile],'$.workPhone') END,' ',''),CHAR(9),''),'-',''),'(',''),')',''),'.','') IN (@p0,@p1,@p2)
            OR REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(CASE WHEN @other=1 THEN JSON_VALUE([Profile],'$.otherPhone') END,' ',''),CHAR(9),''),'-',''),'(',''),')',''),'.','') IN (@p0,@p1,@p2))
            """,
            new SqlParameter("@mobile", fields.Contains("mobilePhone")), new SqlParameter("@work", fields.Contains("workPhone")), new SqlParameter("@other", fields.Contains("otherPhone")),
            new SqlParameter("@ws", access.Trusted.WorkspaceId), new SqlParameter("@p0", forms[0]),
            new SqlParameter("@p1", forms.Count > 1 ? forms[1] : forms[0]), new SqlParameter("@p2", forms.Count > 2 ? forms[2] : forms[0])).AsNoTracking();
        if (access.Authorization.ScopeFilter == RecordAccessScopeFilter.OwnedByMember)
        {
            if (access.Authorization.ScopeOwnerMemberId is not { } owner) return [];
            query = query.Where(c => c.OwnerId == owner);
        }
        var matches = new List<CustomerPhoneCandidate>();
        await foreach (var contact in query.AsAsyncEnumerable().WithCancellation(ct))
        {
            if (await authorization.EnforceRecordAsync(access, contact, "checkDirectCustomerPhone", metadata, ct) is not null) continue;
            var document = ContactProjection.Document(contact);
            var numbers = new Dictionary<string, string?> { ["mobilePhone"] = document.MobilePhone, ["workPhone"] = document.WorkPhone, ["otherPhone"] = document.OtherPhone };
            if (!fields.Any(field => CustomerPhoneComparison.Normalize(numbers[field]) == normalizedPhone)) continue;
            matches.Add(new(new(BuyerType, contact.ContactId), contact.FullName, contact.ArchivedAt is not null || contact.Status == "archived"));
            if (matches.Count > 100) throw new BusinessCommandException("CUSTOMER_PHONE_MATCH_LIMIT", 422);
        }
        return matches;
    }

    public async Task<string> CreateAsync(OwnerTransaction transaction, CreateDirectCustomerRequest request, string key,
        string requestId, string correlationId, CancellationToken ct)
    {
        var grant = await authorization.AuthorizeAsync(new(requestId, correlationId), ContactCapabilities.Create, ct);
        if (!grant.IsSuccess) throw new BusinessCommandException(grant.Error!.Code, grant.Error.Status);
        var result = await create.HandleAsync(new(new CreateContactRequest(FullName: request.Name,
            OwnerId: grant.Value!.Trusted.MemberId, WorkEmail: request.Email, MobilePhone: request.Phone,
            Address: request.Address, Notes: request.Notes, Source: "direct_customer"), new(requestId, correlationId, key, null)), ct, transaction);
        if (!result.IsSuccess)
        {
            if (result.Error!.FieldErrors is { } errors) throw new CustomerSourceValidationException(errors.ToDictionary(
                e => e.Key switch { "fullName" => "name", "workEmail" => "email", "mobilePhone" => "phone", _ => e.Key }, e => e.Value));
            throw new BusinessCommandException(result.Error.Code, result.Error.Status);
        }
        return result.Value!.AggregateId;
    }
}
