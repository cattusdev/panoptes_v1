using System.Security.Claims;
using Panoptes_v1.Constants;

namespace Panoptes_v1.Services.Access
{
    // Ποια projects βλέπει ένας χρήστης, ως φίλτρο για queries (λίστες, αναζήτηση, exports, widgets, αρχεία),
    // ποτέ ως έλεγχος αφού φορτωθούν τα δεδομένα.
    //   All  -> όλα (π.χ. Admin)
    //   Ids  -> μόνο αυτά (query της βάσης, π.χ. από τα μέλη των restricted projects)
    //   κανένα από τα δύο -> κανένα
    public sealed record ProjectScope(bool All, IQueryable<Guid>? Ids)
    {
        public static readonly ProjectScope Everything = new(true, null);
        public static readonly ProjectScope Nothing = new(false, null);
        public static ProjectScope Only(IQueryable<Guid> ids) => new(false, ids);
    }

    public interface IProjectAccess
    {
        ProjectScope VisibleProjects(ClaimsPrincipal user);
    }

    // Μέχρι να υπάρξει το module των projects: ό,τι ανήκει σε project το βλέπει μόνο ο Admin (fail closed).
    // Το module των projects θα το αντικαταστήσει (internal: όσοι έχουν δικαίωμα, restricted: μόνο τα μέλη).
    public class AdminOnlyProjectAccess : IProjectAccess
    {
        public ProjectScope VisibleProjects(ClaimsPrincipal user) =>
            user.IsInRole(SystemRoles.Admin) ? ProjectScope.Everything : ProjectScope.Nothing;
    }
}
