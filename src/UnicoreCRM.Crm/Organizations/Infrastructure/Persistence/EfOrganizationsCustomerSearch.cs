using UnicoreCRM.Crm.Customers.Contracts;

namespace UnicoreCRM.Crm.Organizations.Infrastructure.Persistence;

internal sealed partial class EfOrganizationsPersistence
{
    public CustomerSourceSearchPlan CustomerSearchPlan(string workspaceId, string? ownerId, IReadOnlySet<string> readableFields)
    {
        var parameters = new Dictionary<string, object?> { ["@org_ws"] = workspaceId, ["@org_owner"] = ownerId };
        foreach (var field in new[] { "displayName", "legalName", "email", "phone", "taxCode" })
            parameters["@org_" + field] = readableFields.Contains(field);
        return new("""
            SELECT OrganizationId AS SourceId FROM organizations.Organizations
            WHERE WorkspaceId=@org_ws AND (@org_owner IS NULL OR OwnerId=@org_owner) AND (
            (@org_displayName=1 AND CHARINDEX(@search,UPPER(DisplayName) COLLATE Latin1_General_100_BIN2)>0)
            OR (@org_legalName=1 AND CHARINDEX(@search,UPPER(JSON_VALUE(Profile,'$.legalName')) COLLATE Latin1_General_100_BIN2)>0)
            OR (@org_email=1 AND CHARINDEX(@search,UPPER(JSON_VALUE(Profile,'$.email')) COLLATE Latin1_General_100_BIN2)>0)
            OR (@org_phone=1 AND CHARINDEX(@search,JSON_VALUE(Profile,'$.phone') COLLATE Latin1_General_100_BIN2)>0)
            OR (@org_taxCode=1 AND CHARINDEX(@search,UPPER(JSON_VALUE(Profile,'$.taxCode')) COLLATE Latin1_General_100_BIN2)>0))
            """, parameters);
    }
}
