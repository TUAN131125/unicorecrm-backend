using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Infrastructure.Persistence;

namespace UnicoreCRM.Operations.Tasks;

internal static class TasksModule
{
    internal static IServiceCollection AddTasksModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("UnicoreCRM");
        services.AddDbContext<TasksDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "tasks")));
        services.AddScoped<ITasksPersistence, EfTasksPersistence>();
        services.AddDevelopmentSchemaMigration(
            "tasks",
            (provider, cancellationToken) => provider.GetRequiredService<TasksDbContext>().Database.MigrateAsync(cancellationToken));
        services.AddScoped<TaskAuthorization>();
        services.AddScoped<TaskMutationExecution>();
        services.AddScoped<Contracts.ITaskSummaryReader, Application.ReadTaskSummary.TaskSummaryReader>();
        services.AddScoped<Application.ListTasks.Handler>();
        services.AddScoped<Application.GetTask.Handler>();
        services.AddScoped<Application.ListActivities.Handler>();
        services.AddScoped<Application.CreateTask.Handler>();
        services.AddScoped<Application.CompleteTask.Handler>();
        services.AddScoped<Application.CancelTask.Handler>();
        services.AddScoped<Application.AssignTask.Handler>();
        services.AddScoped<Application.RescheduleTask.Handler>();
        services.AddScoped<Application.ArchiveTask.Handler>();
        services.AddScoped<Application.LogActivity.Handler>();
        return services;
    }
}
