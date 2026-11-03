using UnicoreCRM.Crm.Customers.Contracts;

namespace UnicoreCRM.Crm.Contacts.Infrastructure.Persistence;

internal sealed partial class EfContactsPersistence
{
    public CustomerSourceSearchPlan CustomerSearchPlan(string workspaceId, string? ownerId, IReadOnlySet<string> readableFields)
    {
        var parameters = new Dictionary<string, object?> { ["@ct_ws"] = workspaceId, ["@ct_owner"] = ownerId };
        foreach (var field in new[] { "fullName", "displayName", "personalEmail", "workEmail", "mobilePhone", "workPhone", "otherPhone" })
            parameters["@ct_" + field] = readableFields.Contains(field);
        return new("""
            SELECT ContactId AS SourceId FROM contacts.Contacts
            WHERE WorkspaceId=@ct_ws AND (@ct_owner IS NULL OR OwnerId=@ct_owner) AND (
            (@ct_fullName=1 AND CHARINDEX(@search,UPPER(FullName) COLLATE Latin1_General_100_BIN2)>0)
            OR (@ct_displayName=1 AND CHARINDEX(@search,UPPER(JSON_VALUE(Profile,'$.displayName')) COLLATE Latin1_General_100_BIN2)>0)
            OR (@ct_personalEmail=1 AND CHARINDEX(@search,UPPER(JSON_VALUE(Profile,'$.personalEmail')) COLLATE Latin1_General_100_BIN2)>0)
            OR (@ct_workEmail=1 AND CHARINDEX(@search,UPPER(JSON_VALUE(Profile,'$.workEmail')) COLLATE Latin1_General_100_BIN2)>0)
            OR (@ct_mobilePhone=1 AND CHARINDEX(@search,JSON_VALUE(Profile,'$.mobilePhone') COLLATE Latin1_General_100_BIN2)>0)
            OR (@ct_workPhone=1 AND CHARINDEX(@search,JSON_VALUE(Profile,'$.workPhone') COLLATE Latin1_General_100_BIN2)>0)
            OR (@ct_otherPhone=1 AND CHARINDEX(@search,JSON_VALUE(Profile,'$.otherPhone') COLLATE Latin1_General_100_BIN2)>0))
            """, parameters);
    }
}
