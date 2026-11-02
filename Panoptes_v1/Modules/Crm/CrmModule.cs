using Panoptes_v1.Modules.Crm.Entities;
using Panoptes_v1.Modules.Crm.Services;
using Panoptes_v1.Services.Access;
using Panoptes_v1.Services.Search;

namespace Panoptes_v1.Modules.Crm
{
    // CRM (ARCHITECTURE §4.1): πελάτες, πρόσωπα, οι σχέσεις τους, διπλότυπα, αναζήτηση
    public static class CrmModule
    {
        public static IServiceCollection AddCrmModule(this IServiceCollection services)
        {
            services.AddScoped<CrmAccess>();
            services.AddScoped<IRecordAccess<Client>>(sp => sp.GetRequiredService<CrmAccess>());
            services.AddScoped<IRecordAccess<Person>>(sp => sp.GetRequiredService<CrmAccess>());
            services.AddScoped<ClientService>();
            services.AddScoped<PersonService>();
            services.AddScoped<ISearchProvider, CrmSearch>();
            return services;
        }
    }
}
