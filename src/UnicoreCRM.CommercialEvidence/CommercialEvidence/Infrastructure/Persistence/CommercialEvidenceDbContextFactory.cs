using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UnicoreCRM.CommercialEvidence.CommercialEvidence.Infrastructure.Persistence;

internal sealed class CommercialEvidenceDbContextFactory
    : IDesignTimeDbContextFactory<CommercialEvidenceDbContext>
{
    public CommercialEvidenceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__UnicoreCRM")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=UnicoreCRM;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<CommercialEvidenceDbContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", CommercialEvidenceDbContext.Schema))
            .Options;
        return new(options);
    }
}
