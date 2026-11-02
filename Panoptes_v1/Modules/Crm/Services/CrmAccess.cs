using System.Security.Claims;
using Panoptes_v1.Constants;
using Panoptes_v1.Data;
using Panoptes_v1.Modules.Crm.Entities;
using Panoptes_v1.Services.Access;

namespace Panoptes_v1.Modules.Crm.Services
{
    // Πελάτες και πρόσωπα τα βλέπει όποιος έχει Clients.View, όλα (δεν υπάρχουν "κρυφοί" πελάτες, ARCHITECTURE §3.3).
    // Ό,τι ανήκει σε restricted project (projects, εργασίες, αρχεία) φιλτράρεται από το δικό του module.
    public class CrmAccess : IRecordAccess<Client>, IRecordAccess<Person>
    {
        private readonly ApplicationDbContext _db;

        public CrmAccess(ApplicationDbContext db)
        {
            _db = db;
        }

        public IQueryable<Client> Visible(ClaimsPrincipal user) =>
            Permissions.Has(user, Permissions.Clients.View) ? _db.Set<Client>() : _db.Set<Client>().Where(_ => false);

        IQueryable<Person> IRecordAccess<Person>.Visible(ClaimsPrincipal user) =>
            Permissions.Has(user, Permissions.Clients.View) ? _db.Set<Person>() : _db.Set<Person>().Where(_ => false);
    }
}
