using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Panoptes_v1.Constants;
using Panoptes_v1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Panoptes_v1.Api
{
    public record RoleListItemDto(string Id, string Name, bool IsSystem, int PermissionCount, int UserCount, bool CanManage);

    public record PermissionDto(string Name, string Action);

    public record PermissionModuleDto(string Module, IReadOnlyList<PermissionDto> Permissions);

    public record RoleMemberDto(string Id, string Email, string? AvatarUrl);

    public record RoleDetailDto(
        string Id,
        string Name,
        bool IsSystem,
        bool CanManage,
        // Γιατί δεν μπορεί να τον πειράξει (για να το εξηγήσει το UI)
        string? ReadOnlyReason,
        IReadOnlyList<string> Permissions,
        IReadOnlyList<string> GrantablePermissions,
        IReadOnlyList<PermissionModuleDto> Catalog,
        IReadOnlyList<RoleMemberDto> Members);

    public class RoleNameRequest
    {
        [Required, StringLength(64, MinimumLength = 2)]
        public string Name { get; set; } = "";
    }

    public class RolePermissionsRequest
    {
        public List<string> Permissions { get; set; } = new();
    }

    [ApiController]
    [Route("api/roles")]
    [Authorize]
    public class RolesController : ControllerBase
    {
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly AdminGuard _guard;
        private readonly ProfileService _profiles;
        private readonly IStringLocalizer<SharedResource> _t;

        public RolesController(RoleManager<IdentityRole> roleManager, UserManager<IdentityUser> userManager, AdminGuard guard, ProfileService profiles,
            IStringLocalizer<SharedResource> localizer)
        {
            _t = localizer;
            _profiles = profiles;
            _roleManager = roleManager;
            _userManager = userManager;
            _guard = guard;
        }

        [HttpGet]
        [Authorize(Policy = Permissions.Roles.View)]
        public async Task<ActionResult<IReadOnlyList<RoleListItemDto>>> List()
        {
            var actor = await _userManager.GetUserAsync(User);
            if (actor == null) return Unauthorized();

            var result = new List<RoleListItemDto>();
            foreach (var role in _roleManager.Roles.OrderBy(r => r.Name).ToList())
            {
                result.Add(new RoleListItemDto(
                    role.Id,
                    role.Name ?? "",
                    role.Name == SystemRoles.Admin,
                    (await _guard.GetRolePermissionsAsync(role)).Count,
                    (await _userManager.GetUsersInRoleAsync(role.Name!)).Count,
                    await _guard.CanManageRoleAsync(actor, role)));
            }
            return result;
        }

        [HttpGet("{id}")]
        [Authorize(Policy = Permissions.Roles.View)]
        public async Task<ActionResult<RoleDetailDto>> Get(string id)
        {
            var actor = await _userManager.GetUserAsync(User);
            if (actor == null) return Unauthorized();

            var role = await _roleManager.FindByIdAsync(id);
            if (role == null) return NotFound();

            return await ToDetailAsync(role, actor);
        }

        [HttpPost]
        [Authorize(Policy = Permissions.Roles.Create)]
        public async Task<ActionResult<RoleDetailDto>> Create(RoleNameRequest request)
        {
            var actor = await _userManager.GetUserAsync(User);
            if (actor == null) return Unauthorized();

            var name = request.Name.Trim();
            if (await _roleManager.RoleExistsAsync(name))
                return NameTaken();

            var role = new IdentityRole(name);
            var result = await _roleManager.CreateAsync(role);
            if (!result.Succeeded) return NameProblem(result);

            return CreatedAtAction(nameof(Get), new { id = role.Id }, await ToDetailAsync(role, actor));
        }

        [HttpPut("{id}")]
        [Authorize(Policy = Permissions.Roles.Edit)]
        public async Task<ActionResult<RoleDetailDto>> Rename(string id, RoleNameRequest request)
        {
            var actor = await _userManager.GetUserAsync(User);
            if (actor == null) return Unauthorized();

            var role = await _roleManager.FindByIdAsync(id);
            if (role == null) return NotFound();

            var denied = await DenyIfCannotManageAsync(actor, role);
            if (denied != null) return denied;

            var name = request.Name.Trim();
            if (!string.Equals(role.Name, name, StringComparison.OrdinalIgnoreCase) && await _roleManager.RoleExistsAsync(name))
                return NameTaken();

            // Το όνομα του ρόλου είναι μέσα στα cookies των χρηστών του
            var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name!);

            role.Name = name;
            var result = await _roleManager.UpdateAsync(role);
            if (!result.Succeeded) return NameProblem(result);

            await _guard.RefreshRoleSessionsAsync(usersInRole, actor.Id);
            return await ToDetailAsync(role, actor);
        }

        [HttpPut("{id}/permissions")]
        [Authorize(Policy = Permissions.Roles.Edit)]
        public async Task<ActionResult<RoleDetailDto>> SetPermissions(string id, RolePermissionsRequest request)
        {
            var actor = await _userManager.GetUserAsync(User);
            if (actor == null) return Unauthorized();

            var role = await _roleManager.FindByIdAsync(id);
            if (role == null) return NotFound();

            var denied = await DenyIfCannotManageAsync(actor, role);
            if (denied != null) return denied;

            // Κρατάμε μόνο πραγματικά permissions (τα ονόματα έρχονται από τον browser!)
            var selected = request.Permissions.Intersect(Permissions.GetAll()).ToHashSet();

            // Δεν δίνεις permissions που δεν έχεις ο ίδιος
            if (!await _guard.IsAdminAsync(actor) && !selected.IsSubsetOf(await _guard.GetUserPermissionsAsync(actor)))
                return Problem(_t["You cannot grant permissions you don't have yourself."], statusCode: StatusCodes.Status403Forbidden);

            foreach (var claim in (await _roleManager.GetClaimsAsync(role)).Where(c => c.Type == "Permission"))
                await _roleManager.RemoveClaimAsync(role, claim);

            foreach (var permission in selected)
                await _roleManager.AddClaimAsync(role, new Claim("Permission", permission));

            // Όλοι οι χρήστες του ρόλου παίρνουν τα νέα permissions αμέσως
            await _guard.RefreshRoleSessionsAsync(await _userManager.GetUsersInRoleAsync(role.Name!), actor.Id);

            return await ToDetailAsync(role, actor);
        }

        [HttpDelete("{id}")]
        [Authorize(Policy = Permissions.Roles.Delete)]
        public async Task<IActionResult> Delete(string id)
        {
            var actor = await _userManager.GetUserAsync(User);
            if (actor == null) return Unauthorized();

            var role = await _roleManager.FindByIdAsync(id);
            if (role == null) return NotFound();

            var denied = await DenyIfCannotManageAsync(actor, role);
            if (denied != null) return denied;

            var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name!);
            var result = await _roleManager.DeleteAsync(role);
            if (!result.Succeeded) return NameProblem(result);

            await _guard.RefreshRoleSessionsAsync(usersInRole, actor.Id);
            return NoContent();
        }

        private async Task<ActionResult?> DenyIfCannotManageAsync(IdentityUser actor, IdentityRole role)
        {
            if (await _guard.CanManageRoleAsync(actor, role)) return null;
            return Problem(await ReadOnlyReasonAsync(actor, role), statusCode: StatusCodes.Status403Forbidden);
        }

        private async Task<string> ReadOnlyReasonAsync(IdentityUser actor, IdentityRole role)
        {
            if (role.Name == SystemRoles.Admin)
                return _t["Administrator is a protected system role. It always has every permission and cannot be changed."];
            if (await _userManager.IsInRoleAsync(actor, role.Name!))
                return _t["You can't change a role you belong to."];
            return _t["This role has permissions you don't have, so you can't change it."];
        }

        private async Task<RoleDetailDto> ToDetailAsync(IdentityRole role, IdentityUser actor)
        {
            var canManage = await _guard.CanManageRoleAsync(actor, role);
            var grantable = await _guard.IsAdminAsync(actor)
                ? Permissions.GetAll()
                : (await _guard.GetUserPermissionsAsync(actor)).ToList();

            var catalog = Permissions.GetModules()
                .Select(m => new PermissionModuleDto(m.Module, m.Permissions.Select(p => new PermissionDto(p.Name, p.Action)).ToList()))
                .ToList();

            var users = await _userManager.GetUsersInRoleAsync(role.Name!);
            var avatars = await _profiles.AvatarUrlsAsync(users.Select(u => u.Id));
            var members = users
                .OrderBy(u => u.Email)
                .Select(u => new RoleMemberDto(u.Id, u.Email ?? "", avatars.GetValueOrDefault(u.Id)))
                .ToList();

            return new RoleDetailDto(
                role.Id,
                role.Name ?? "",
                role.Name == SystemRoles.Admin,
                canManage,
                canManage ? null : await ReadOnlyReasonAsync(actor, role),
                (await _guard.GetRolePermissionsAsync(role)).ToList(),
                grantable,
                catalog,
                members);
        }

        private ActionResult NameTaken()
        {
            ModelState.AddModelError(nameof(RoleNameRequest.Name), _t["A role with this name already exists."]);
            return ValidationProblem(ModelState);
        }

        private ActionResult NameProblem(IdentityResult result)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(nameof(RoleNameRequest.Name), error.Description);
            return ValidationProblem(ModelState);
        }
    }
}
