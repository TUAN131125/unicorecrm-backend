using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Infrastructure.Persistence;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Contacts;

internal static class ContactsModule
{
    internal static IServiceCollection AddContactsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("UnicoreCRM");
        services.AddDbContext<ContactsDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "contacts")));
        services.AddScoped<IContactsPersistence, EfContactsPersistence>();
        services.AddDevelopmentSchemaMigration(
            "contacts",
            (provider, cancellationToken) => provider.GetRequiredService<ContactsDbContext>().Database.MigrateAsync(cancellationToken));
        services.AddScoped<ContactAuthorization>();
        services.AddScoped<Application.ListContacts.Handler>();
        services.AddScoped<Application.GetContact.Handler>();
        services.AddScoped<Application.CreateContact.Handler>();
        services.AddScoped<Application.UpdateContact.Handler>();
        services.AddScoped<Application.ArchiveContact.Handler>();
        services.AddScoped<Application.GetRelationshipSummary.Handler>();
        services.AddScoped<Application.Relationships.Handler>();
        services.AddScoped<Contracts.IOrganizationContactReadParticipant, Application.ReadOrganizationContacts.Participant>();
        services.AddScoped<Contracts.IContactCustomerSubjectParticipant, Application.CustomerParticipants.SubjectParticipant>();
        services.AddScoped<Contracts.ICustomerStakeholderReadParticipant, Application.CustomerParticipants.StakeholderParticipant>();
        services.AddScoped<Contracts.ILeadCustomerStakeholderParticipant, Application.CustomerParticipants.LeadConversionStakeholderParticipant>();
        // The Lead qualification participant. It is an internal owner boundary consumed by the
        // Workflows coordinator; it maps no route and widens no public Contacts surface.
        services.AddScoped<Contracts.IContactQualificationParticipant,
            Application.ResolveQualificationContact.Handler>();
        services.AddScoped<IRecordAccessFactProvider, Application.ProvideContactRecordAccessFacts.ContactRecordAccessFactProvider>();
        return services;
    }
}
