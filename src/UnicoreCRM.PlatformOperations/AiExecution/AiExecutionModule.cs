using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.PlatformOperations.AiExecution.Contracts;
using UnicoreCRM.PlatformOperations.AiExecution.Infrastructure;
namespace UnicoreCRM.PlatformOperations.AiExecution;
internal static class AiExecutionModule
{
    internal static IServiceCollection AddAiExecutionModule(this IServiceCollection services,IConfiguration configuration){services.AddDbContext<AiExecutionDbContext>(options=>options.UseSqlServer(configuration.GetConnectionString("UnicoreCRM"),sql=>sql.MigrationsHistoryTable("__EFMigrationsHistory","platform_ai")));services.AddScoped<IAiExecutionLedger,EfAiExecutionLedger>();services.AddScoped<IWorkspaceAiConfigurationStore,EfWorkspaceAiConfigurationStore>();services.AddScoped<IProactiveStore,EfProactiveStore>();services.AddDevelopmentSchemaMigration("platform_ai",(provider,ct)=>provider.GetRequiredService<AiExecutionDbContext>().Database.MigrateAsync(ct));return services;}
}
