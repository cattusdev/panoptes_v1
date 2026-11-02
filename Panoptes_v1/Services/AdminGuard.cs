using Panoptes_v1.Constants;
using Microsoft.AspNetCore.Identity;

namespace Panoptes_v1.Services
{
    // Ιεραρχία δικαιωμάτων: κανείς δεν μπορεί να πειράξει κάποιον/κάτι πιο "ισχυρό" από τον εαυτό του.
    // Τα permissions διαβάζονται από τη βάση (όχι από το cookie) ώστε να είναι πάντα φρέσκα.
    public class AdminGuard
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly SignInManager<IdentityUser> _signInManager;

        public AdminGuard(
            UserManager<IdentityUser> userManager,
            RoleManager<IdentityRole> roleManager,
            SignInManager<IdentityUser> signInManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _signInManager = signInManager;
        }

        public Task<bool> IsAdminAsync(IdentityUser user) => _userManager.IsInRoleAsync(user, SystemRoles.Admin);

        public async Task<HashSet<string>> GetRolePermissionsAsync(IdentityRole role)
        {
            if (role.Name == SystemRoles.Admin)
                return Permissions.GetAll().ToHashSet();

            var claims = await _roleManager.GetClaimsAsync(role);
            return claims.Where(c => c.Type == "Permission").Select(c => c.Value).ToHashSet();
        }

        // Όλα τα permissions που έχει ο χρήστης μέσω των ρόλων του
        public async Task<HashSet<string>> GetUserPermissionsAsync(IdentityUser user)
        {
            var permissions = new HashSet<string>();
            foreach (var roleName in await _userManager.GetRolesAsync(user))
            {
                var role = await _roleManager.FindByNameAsync(roleName);
                if (role != null)
                    permissions.UnionWith(await GetRolePermissionsAsync(role));
            }
            return permissions;
        }

        // Edit / Reset Password / Delete: μόνο αν ο target δεν έχει τίποτα παραπάνω από τον actor
        public async Task<bool> CanManageUserAsync(IdentityUser actor, IdentityUser target)
        {
            if (await IsAdminAsync(actor)) return true;
            if (await IsAdminAsync(target)) return false;

            var targetPermissions = await GetUserPermissionsAsync(target);
            return targetPermissions.IsSubsetOf(await GetUserPermissionsAsync(actor));
        }

        // Δίνεις ρόλο μόνο αν έχεις ήδη όλα τα permissions του (κενό = "No Role", πάντα επιτρέπεται)
        public async Task<bool> CanAssignRoleAsync(IdentityUser actor, string? roleName)
        {
            if (string.IsNullOrEmpty(roleName)) return true;

            var role = await _roleManager.FindByNameAsync(roleName);
            if (role == null) return false;
            if (await IsAdminAsync(actor)) return true;
            if (role.Name == SystemRoles.Admin) return false;

            var rolePermissions = await GetRolePermissionsAsync(role);
            return rolePermissions.IsSubsetOf(await GetUserPermissionsAsync(actor));
        }

        // Rename / Delete / Permissions ενός ρόλου
        public async Task<bool> CanManageRoleAsync(IdentityUser actor, IdentityRole role)
        {
            if (role.Name == SystemRoles.Admin) return false; // System role: κλειδωμένος για όλους
            if (await IsAdminAsync(actor)) return true;
            if (await _userManager.IsInRoleAsync(actor, role.Name!)) return false; // Όχι στον δικό σου ρόλο

            var rolePermissions = await GetRolePermissionsAsync(role);
            return rolePermissions.IsSubsetOf(await GetUserPermissionsAsync(actor));
        }

        // Οι ρόλοι/permissions ζουν μέσα στο cookie. Αλλάζοντας το SecurityStamp,
        // το cookie απορρίπτεται στον επόμενο έλεγχο και ο χρήστης ξαναδιαβάζεται από τη βάση.
        public async Task RefreshUserSessionAsync(IdentityUser user, string? currentUserId)
        {
            await _userManager.UpdateSecurityStampAsync(user);

            // Τον εαυτό μας δεν τον πετάμε έξω: απλά του βγάζουμε νέο cookie
            if (user.Id == currentUserId)
                await _signInManager.RefreshSignInAsync(user);
        }

        public async Task RefreshRoleSessionsAsync(IList<IdentityUser> usersInRole, string? currentUserId)
        {
            foreach (var user in usersInRole)
                await RefreshUserSessionAsync(user, currentUserId);
        }
    }
}
