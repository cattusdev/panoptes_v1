using System.ComponentModel.DataAnnotations;
using Panoptes_v1.Constants;
using Panoptes_v1.Data;
using Panoptes_v1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Panoptes_v1.Api
{
    public record UserListItemDto(string Id, string Email, IReadOnlyList<string> Roles, bool EmailConfirmed, bool IsLockedOut, bool IsSelf, bool CanManage, string? AvatarUrl);

    public record UserDetailDto(
        string Id,
        string Email,
        string? Role,
        bool IsAdmin,
        bool EmailConfirmed,
        bool IsLockedOut,
        DateTimeOffset? LockoutEnd,
        bool TwoFactorEnabled,
        bool IsSelf,
        bool CanManage,
        string? AvatarUrl);

    public enum UserStatusFilter { Active, Locked, Unconfirmed }

    public class UserListRequest : ListRequest
    {
        public UserListRequest() => PageSize = 20;

        public string? Role { get; set; }
        public UserStatusFilter? Status { get; set; }
    }

    public record RoleOptionsDto(IReadOnlyList<string> All, IReadOnlyList<string> Assignable);

    public class CreateUserRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        public string Password { get; set; } = "";

        public string? Role { get; set; }
    }

    public class UpdateUserRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";

        public string? Role { get; set; }
    }

    public class ResetPasswordRequest
    {
        [Required]
        public string NewPassword { get; set; } = "";
    }

    [ApiController]
    [Route("api/users")]
    [Authorize]
    public class UsersController : ControllerBase
    {
        private string NotAllowedToManageUser => _t["You don't have enough privileges to manage this user."];
        private string NotAllowedToAssignRole => _t["You cannot assign a role with permissions you don't have."];

        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly AdminGuard _guard;
        private readonly ProfileService _profiles;
        private readonly IStringLocalizer<SharedResource> _t;

        public UsersController(
            ApplicationDbContext context,
            UserManager<IdentityUser> userManager,
            RoleManager<IdentityRole> roleManager,
            AdminGuard guard,
            ProfileService profiles,
            IStringLocalizer<SharedResource> localizer)
        {
            _t = localizer;
            _profiles = profiles;
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _guard = guard;
        }

        // Ταξινόμηση της λίστας: email (προεπιλογή)
        private static readonly SortMap<IdentityUser> UserSort = new SortMap<IdentityUser>("email", u => u.Id).Add("email", u => u.Email);

        // GET /api/users?search=&role=&status=active|locked|unconfirmed&sort=email|-email&page=&pageSize=
        [HttpGet]
        [Authorize(Policy = Permissions.Users.View)]
        public async Task<ActionResult<PagedResult<UserListItemDto>>> List([FromQuery] UserListRequest request)
        {
            var actor = await _userManager.GetUserAsync(User);
            if (actor == null) return Unauthorized();

            var now = DateTimeOffset.UtcNow;
            var query = _context.Users.AsQueryable();

            if (request.SearchTerm is string term)
            {
                var pattern = Like.Contains(term);
                query = query.Where(u => u.Email != null && EF.Functions.ILike(u.Email, pattern));
            }

            if (!string.IsNullOrWhiteSpace(request.Role))
            {
                var roleEntity = await _roleManager.FindByNameAsync(request.Role);
                var roleId = roleEntity?.Id ?? "";
                query = query.Where(u => _context.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == roleId));
            }

            query = request.Status switch
            {
                UserStatusFilter.Locked => query.Where(u => u.LockoutEnd != null && u.LockoutEnd > now),
                UserStatusFilter.Unconfirmed => query.Where(u => !u.EmailConfirmed),
                UserStatusFilter.Active => query.Where(u => u.EmailConfirmed && (u.LockoutEnd == null || u.LockoutEnd <= now)),
                _ => query
            };

            var page = await UserSort.Apply(query, request.Sort).ToPagedAsync(request);
            var users = page.Items;

            var avatars = await _profiles.AvatarUrlsAsync(users.Select(u => u.Id));
            var items = new List<UserListItemDto>();
            foreach (var user in users)
            {
                items.Add(new UserListItemDto(
                    user.Id,
                    user.Email ?? "",
                    (await _userManager.GetRolesAsync(user)).ToList(),
                    user.EmailConfirmed,
                    IsLockedOut(user),
                    user.Id == actor.Id,
                    await _guard.CanManageUserAsync(actor, user),
                    avatars.GetValueOrDefault(user.Id)));
            }

            return new PagedResult<UserListItemDto>(items, page.Total, page.Page, page.PageSize);
        }

        // All: όλοι οι ρόλοι (για φίλτρα). Assignable: όσοι μπορεί να δώσει ο τρέχων χρήστης (για τα dropdowns)
        [HttpGet("role-options")]
        [Authorize(Policy = Permissions.Users.View)]
        public async Task<ActionResult<RoleOptionsDto>> RoleOptions()
        {
            var actor = await _userManager.GetUserAsync(User);
            if (actor == null) return Unauthorized();

            var all = _roleManager.Roles.OrderBy(r => r.Name).Select(r => r.Name!).ToList();
            var assignable = new List<string>();
            foreach (var roleName in all)
            {
                if (await _guard.CanAssignRoleAsync(actor, roleName))
                    assignable.Add(roleName);
            }
            return new RoleOptionsDto(all, assignable);
        }

        [HttpGet("{id}")]
        [Authorize(Policy = Permissions.Users.View)]
        public async Task<ActionResult<UserDetailDto>> Get(string id)
        {
            var actor = await _userManager.GetUserAsync(User);
            if (actor == null) return Unauthorized();

            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            return await ToDetailAsync(user, actor);
        }

        [HttpPost]
        [Authorize(Policy = Permissions.Users.Create)]
        public async Task<ActionResult<UserDetailDto>> Create(CreateUserRequest request)
        {
            var actor = await _userManager.GetUserAsync(User);
            if (actor == null) return Unauthorized();

            // --- SECURITY CHECK: ΙΕΡΑΡΧΙΑ ---
            if (!await _guard.CanAssignRoleAsync(actor, request.Role))
                return Problem(NotAllowedToAssignRole, statusCode: StatusCodes.Status403Forbidden);

            var user = new IdentityUser
            {
                UserName = request.Email,
                Email = request.Email,
                EmailConfirmed = true // Ο admin τον φτιάχνει: μπορεί να κάνει login αμέσως
            };

            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded) return IdentityValidationProblem(result);

            if (!string.IsNullOrEmpty(request.Role))
                await _userManager.AddToRoleAsync(user, request.Role);

            return CreatedAtAction(nameof(Get), new { id = user.Id }, await ToDetailAsync(user, actor));
        }

        [HttpPut("{id}")]
        [Authorize(Policy = Permissions.Users.Edit)]
        public async Task<ActionResult<UserDetailDto>> Update(string id, UpdateUserRequest request)
        {
            var actor = await _userManager.GetUserAsync(User);
            if (actor == null) return Unauthorized();

            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            // --- SECURITY CHECK: ΙΕΡΑΡΧΙΑ ---
            if (!await _guard.CanManageUserAsync(actor, user))
                return Problem(NotAllowedToManageUser, statusCode: StatusCodes.Status403Forbidden);

            var currentRoles = await _userManager.GetRolesAsync(user);
            var currentRole = currentRoles.FirstOrDefault() ?? "";
            var selectedRole = request.Role ?? "";
            var roleChanged = selectedRole != currentRole;

            if (roleChanged)
            {
                // Μαζί με το CanManageUser, εγγυάται ότι μένει πάντα τουλάχιστον ένας Admin
                if (user.Id == actor.Id && currentRoles.Contains(SystemRoles.Admin) && selectedRole != SystemRoles.Admin)
                    return Problem(_t["You cannot remove the Administrator role from your own account."], statusCode: StatusCodes.Status400BadRequest);

                if (!await _guard.CanAssignRoleAsync(actor, selectedRole))
                    return Problem(NotAllowedToAssignRole, statusCode: StatusCodes.Status403Forbidden);
            }

            if (user.Email != request.Email)
            {
                user.Email = request.Email;
                user.UserName = request.Email;
                var updateResult = await _userManager.UpdateAsync(user);
                if (!updateResult.Succeeded) return IdentityValidationProblem(updateResult);
            }

            if (roleChanged)
            {
                if (currentRoles.Any())
                    await _userManager.RemoveFromRolesAsync(user, currentRoles);
                if (!string.IsNullOrEmpty(selectedRole))
                    await _userManager.AddToRoleAsync(user, selectedRole);

                // Τα νέα δικαιώματα ισχύουν αμέσως, όχι στο επόμενο login
                await _guard.RefreshUserSessionAsync(user, actor.Id);
            }

            return await ToDetailAsync(user, actor);
        }

        [HttpPost("{id}/reset-password")]
        [Authorize(Policy = Permissions.Users.Edit)]
        public async Task<IActionResult> ResetPassword(string id, ResetPasswordRequest request)
        {
            var actor = await _userManager.GetUserAsync(User);
            if (actor == null) return Unauthorized();

            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            // --- SECURITY CHECK: ΙΕΡΑΡΧΙΑ (αλλιώς = account takeover) ---
            if (!await _guard.CanManageUserAsync(actor, user))
                return Problem(NotAllowedToManageUser, statusCode: StatusCodes.Status403Forbidden);

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, request.NewPassword);
            if (!result.Succeeded) return IdentityValidationProblem(result, defaultField: nameof(ResetPasswordRequest.NewPassword));

            return NoContent();
        }

        [HttpPost("{id}/unlock")]
        [Authorize(Policy = Permissions.Users.Edit)]
        public async Task<ActionResult<UserDetailDto>> Unlock(string id)
        {
            var actor = await _userManager.GetUserAsync(User);
            if (actor == null) return Unauthorized();

            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            if (!await _guard.CanManageUserAsync(actor, user))
                return Problem(NotAllowedToManageUser, statusCode: StatusCodes.Status403Forbidden);

            await _userManager.SetLockoutEndDateAsync(user, null);
            await _userManager.ResetAccessFailedCountAsync(user);

            return await ToDetailAsync(user, actor);
        }

        [HttpPost("{id}/promote")]
        [Authorize(Roles = SystemRoles.Admin)] // Μόνο ένας Admin φτιάχνει άλλον Admin
        public async Task<ActionResult<UserDetailDto>> Promote(string id)
        {
            var actor = await _userManager.GetUserAsync(User);
            if (actor == null) return Unauthorized();

            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            if (!await _userManager.IsInRoleAsync(user, SystemRoles.Admin))
            {
                await _userManager.AddToRoleAsync(user, SystemRoles.Admin);
                await _guard.RefreshUserSessionAsync(user, actor.Id);
            }

            return await ToDetailAsync(user, actor);
        }

        [HttpDelete("{id}")]
        [Authorize(Policy = Permissions.Users.Delete)]
        public async Task<IActionResult> Delete(string id)
        {
            var actor = await _userManager.GetUserAsync(User);
            if (actor == null) return Unauthorized();

            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            if (user.Id == actor.Id)
                return Problem(_t["You cannot delete your own account."], statusCode: StatusCodes.Status400BadRequest);

            if (!await _guard.CanManageUserAsync(actor, user))
                return Problem(NotAllowedToManageUser, statusCode: StatusCodes.Status403Forbidden);

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded) return IdentityValidationProblem(result);
            await _profiles.DeleteAllForUserAsync(user.Id);

            return NoContent();
        }

        private static bool IsLockedOut(IdentityUser user) =>
            user.LockoutEnd != null && user.LockoutEnd > DateTimeOffset.UtcNow;

        private async Task<UserDetailDto> ToDetailAsync(IdentityUser user, IdentityUser actor)
        {
            var roles = await _userManager.GetRolesAsync(user);
            return new UserDetailDto(
                user.Id,
                user.Email ?? "",
                roles.FirstOrDefault(),
                roles.Contains(SystemRoles.Admin),
                user.EmailConfirmed,
                IsLockedOut(user),
                user.LockoutEnd,
                user.TwoFactorEnabled,
                user.Id == actor.Id,
                await _guard.CanManageUserAsync(actor, user),
                await _profiles.AvatarUrlAsync(user.Id));
        }

        // Τα λάθη του Identity ως validation errors ανά πεδίο (Email / Password)
        private ActionResult IdentityValidationProblem(IdentityResult result, string defaultField = "")
        {
            foreach (var error in result.Errors)
            {
                var field = error.Code.StartsWith("Password") ? (defaultField == "" ? "Password" : defaultField)
                    : error.Code.Contains("Email") || error.Code.Contains("UserName") ? "Email"
                    : defaultField;
                ModelState.AddModelError(field, error.Description);
            }
            return ValidationProblem(ModelState);
        }
    }
}
