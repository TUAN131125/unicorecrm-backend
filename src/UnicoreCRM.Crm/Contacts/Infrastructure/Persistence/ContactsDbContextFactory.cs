using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UnicoreCRM.Crm.Contacts.Infrastructure.Persistence;

internal sealed class ContactsDbContextFactory : IDesignTimeDbContextFactory<ContactsDbContext>
{
    public ContactsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__UnicoreCRM")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=UnicoreCRM;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<ContactsDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "contacts"))
            .Options;
        return new ContactsDbContext(options);
    }
}
