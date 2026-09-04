using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence;

internal sealed class WorkflowsDbContextFactory : IDesignTimeDbContextFactory<WorkflowsDbContext>
{
    public WorkflowsDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var connectionString = configuration.GetConnectionString("UnicoreCRM")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=UnicoreCRM_Design;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<WorkflowsDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "workflow"))
            .Options;
        return new WorkflowsDbContext(options);
    }
}
