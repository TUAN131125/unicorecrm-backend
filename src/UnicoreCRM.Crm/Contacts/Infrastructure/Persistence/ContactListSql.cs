using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Crm.Contacts.Application.ListContacts;
using UnicoreCRM.Crm.Contacts.Domain;

namespace UnicoreCRM.Crm.Contacts.Infrastructure.Persistence;

internal static class ContactListSql
{
    // Profile is a JSON value-converted column, not a queryable EF owned entity. SQL JSON_VALUE
    // predicates stay on the provider; only bounded Contact rows are deserialized after paging.
    internal static IQueryable<Contact> Filter(ContactsDbContext db, ContactListSpecification specification)
    {
        var parameters = new List<object>();
        string Bind(object value) { var name = $"@p{parameters.Count}"; parameters.Add(new SqlParameter(name, value)); return name; }
        var predicates = new List<string> { $"c.[WorkspaceId] = {Bind(specification.WorkspaceId)}" };
        if (specification.ScopeOwnerId is { } scoped) predicates.Add($"c.[OwnerId] = {Bind(scoped)}");
        var f = specification.Filters;
        predicates.Add(f.Status is { } status ? $"c.[Status] = {Bind(status)}" : "c.[ArchivedAt] IS NULL AND c.[Status] <> N'archived'");
        if (f.OwnerId is { } owner) predicates.Add($"c.[OwnerId] = {Bind(owner)}");
        foreach (var (field, value) in new[] { ("source", f.Source), ("relationshipLevel", f.RelationshipLevel), ("decisionRole", f.DecisionRole) })
            if (value is not null) predicates.Add($"JSON_VALUE(c.[Profile], '$.{field}') = {Bind(value)}");
        if (f.DoNotContact is { } dnc) predicates.Add($"COALESCE(JSON_VALUE(c.[Profile], '$.doNotContact'), N'false') = {Bind(dnc ? "true" : "false")}");
        if (f.Link is { } link)
        {
            var exists = "EXISTS (SELECT 1 FROM [contacts].[CustomerRelationships] r WHERE r.[WorkspaceId] = c.[WorkspaceId] AND r.[ContactId] = c.[ContactId] AND r.[EffectiveTo] IS NULL)";
            predicates.Add(link == "linked" ? exists : $"NOT {exists}");
        }
        if (f.Search is { } search)
        {
            var value = Bind(search.ToUpperInvariant());
            var matches = new List<string> { $"CHARINDEX({value}, UPPER(c.[FullName])) > 0" };
            foreach (var field in specification.SearchFields)
            {
                if (!new[] { "displayName", "workEmail", "personalEmail", "mobilePhone", "workPhone", "otherPhone" }.Contains(field)) throw new InvalidOperationException("Unknown Contact search projection.");
                matches.Add($"CHARINDEX({value}, UPPER(JSON_VALUE(c.[Profile], '$.{field}'))) > 0");
            }
            predicates.Add($"({string.Join(" OR ", matches)})");
        }
        var sql = "SELECT c.* FROM [contacts].[Contacts] c WHERE " + string.Join(" AND ", predicates);
        return db.Contacts.FromSqlRaw(sql, parameters.ToArray()).AsNoTracking();
    }
}
