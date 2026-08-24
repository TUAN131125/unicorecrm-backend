using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Infrastructure.Persistence;

namespace UnicoreCRM.Crm.Leads;

internal static class LeadsModule
{
    internal static IServiceCollection AddLeadsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("UnicoreCRM");
        services.AddDbContext<LeadsDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "leads")));
        services.AddScoped<ILeadsPersistence, EfLeadsPersistence>();
        services.AddScoped<LeadAuthorization>();
        services.AddScoped<Application.CreateLead.LeadCreateExecution>();
        services.AddScoped<Contracts.IInboundLeadIngress, Application.CreateLead.InboundLeadIngress>();
        services.AddScoped<Contracts.ILeadSummaryReader, Application.ReadLeadSummary.LeadSummaryReader>();
        services.AddScoped<LeadMutationExecution>();
        services.AddScoped<Application.ListLeads.Handler>();
        services.AddScoped<Application.GetLead.Handler>();
        services.AddScoped<Application.CreateLead.Handler>();
        services.AddScoped<Application.ReplaceLeadProfile.Handler>();
        services.AddScoped<Application.AdvanceLeadWorkState.Handler>();
        services.AddScoped<Application.DisqualifyLead.Handler>();
        services.AddScoped<Application.ReopenDisqualifiedLead.Handler>();
        return services;
    }
}
