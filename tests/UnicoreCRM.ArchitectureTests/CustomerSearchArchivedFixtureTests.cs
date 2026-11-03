using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Crm.Contacts.Domain;
using UnicoreCRM.Crm.Contacts.Infrastructure.Persistence;
using UnicoreCRM.Crm.Customers.Domain;
using UnicoreCRM.Crm.Customers.Infrastructure.Persistence;
using Xunit;

namespace UnicoreCRM.ArchitectureTests;

/// <summary>Owner-aggregate fixture setup only. Does not grant or impersonate archive authority.</summary>
public sealed class CustomerSearchArchivedFixtureTests
{
    public sealed class FixtureFact:FactAttribute
    { public FixtureFact(){if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CUSTOMER_SEARCH_FIXTURE_CONNECTION")))Skip="Requires an explicitly isolated search fixture database.";} }
    [FixtureFact]
    public async Task Create_archived_subject_through_owner_aggregate_for_HTTP_search_verification()
    {
        var connection=Environment.GetEnvironmentVariable("CUSTOMER_SEARCH_FIXTURE_CONNECTION")!;
        var database=new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection).InitialCatalog;
        Assert.Matches("^UnicoreCRM_QuoteGmailTest_[a-f0-9]{32}$",database);
        var workspace=Environment.GetEnvironmentVariable("CUSTOMER_SEARCH_FIXTURE_WORKSPACE")!;
        var owner=Environment.GetEnvironmentVariable("CUSTOMER_SEARCH_FIXTURE_OWNER")!;
        Assert.False(string.IsNullOrWhiteSpace(workspace));Assert.False(string.IsNullOrWhiteSpace(owner));
        await using var contacts=new ContactsDbContext(new DbContextOptionsBuilder<ContactsDbContext>().UseSqlServer(connection).Options);
        await using var customers=new CustomersDbContext(new DbContextOptionsBuilder<CustomersDbContext>().UseSqlServer(connection).Options);
        var now=DateTimeOffset.UtcNow;
        var contact=new Contact(workspace,owner,"Archived search owner fixture","active",new ContactProfile(),now);
        contacts.Contacts.Add(contact);await contacts.SaveChangesAsync();
        var customer=new Customer(workspace,owner,"CONTACT",contact.ContactId,new CustomerProfile { Segment="ARCHIVED_SEARCH_FIXTURE",SourceSystem="ISOLATED_TEST_FIXTURE" },now);
        customer.Archive(now);
        customers.Customers.Add(customer);
        customers.AuditRecords.Add(new CustomerAuditRecord("searchFixtureSetup",workspace,"test:search-fixture",customer.CustomerId,"search-fixture",contact.ContactId,customer.Version,now));
        await customers.SaveChangesAsync();
        var unassigned=new Contact(workspace,null,"Unassigned source search fixture","active",new ContactProfile(),now);
        contacts.Contacts.Add(unassigned);await contacts.SaveChangesAsync();
        var restricted=new Customer(workspace,owner,"CONTACT",unassigned.ContactId,new CustomerProfile { Segment="RECORD_SCOPE_SEARCH_FIXTURE",SourceSystem="ISOLATED_TEST_FIXTURE" },now);
        customers.Customers.Add(restricted);await customers.SaveChangesAsync();
        await File.WriteAllTextAsync(Environment.GetEnvironmentVariable("CUSTOMER_SEARCH_FIXTURE_OUTPUT")!,JsonSerializer.Serialize(new {
            workspace,contactId=contact.ContactId,customerId=customer.CustomerId,customerCode=customer.CustomerCode,status=customer.Status,
            unassignedContactId=unassigned.ContactId,unassignedCustomerId=restricted.CustomerId,
            fixtureAuthority="Owner aggregate setup in test assembly; not an authorized archive HTTP operation"}));
        Assert.Equal("ARCHIVED",customer.Status);
    }
}
