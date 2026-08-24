using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using UnicoreCRM.Operations.Tasks;
using UnicoreCRM.Operations.Support;

namespace UnicoreCRM.Operations;

public static class OperationsModule
{
    public static IServiceCollection AddOperationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTasksModule(configuration);
        services.AddSupportModule();

        return services;
    }
}
