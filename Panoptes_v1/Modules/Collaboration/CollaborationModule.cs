using Panoptes_v1.Modules.Collaboration.Services;

namespace Panoptes_v1.Modules.Collaboration
{
    // Σημειώσεις, σχόλια, ιστορικό δραστηριότητας, ειδοποιήσεις (ARCHITECTURE §4.5). Προς το παρόν: το ιστορικό.
    public static class CollaborationModule
    {
        public static IServiceCollection AddCollaborationModule(this IServiceCollection services)
        {
            services.AddScoped<IActivityLog, ActivityLog>();
            return services;
        }
    }
}
