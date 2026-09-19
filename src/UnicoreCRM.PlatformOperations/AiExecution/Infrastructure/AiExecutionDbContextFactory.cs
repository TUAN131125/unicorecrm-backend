using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace UnicoreCRM.PlatformOperations.AiExecution.Infrastructure;
internal sealed class AiExecutionDbContextFactory : IDesignTimeDbContextFactory<AiExecutionDbContext>
{
    public AiExecutionDbContext CreateDbContext(string[] args){var options=new DbContextOptionsBuilder<AiExecutionDbContext>().UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=UnicoreCRM;Trusted_Connection=True",x=>x.MigrationsHistoryTable("__EFMigrationsHistory","platform_ai")).Options;return new(options);}
}
