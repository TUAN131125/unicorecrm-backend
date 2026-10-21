using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UnicoreCRM.Communications.Infrastructure.Persistence;

internal sealed class CommunicationsDbContextFactory : IDesignTimeDbContextFactory<CommunicationsDbContext>
{
    public CommunicationsDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("COMMUNICATIONS_DESIGN_CONNECTION")
            ?? throw new InvalidOperationException("Explicit Communications design connection required.");
        return new(new DbContextOptionsBuilder<CommunicationsDbContext>().UseSqlServer(connection,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "communications")).Options);
    }
}
