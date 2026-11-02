using System.Diagnostics;
using Panoptes.Constants;
using Panoptes.Data;
using Panoptes.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Panoptes.Api
{
    public record DashboardMetricsDto(int TotalUsers, int TotalRoles, int Administrators, int AuditEvents24h);

    public record DashboardSecurityDto(int UsersWithFailedLogins, int LockedAccounts, int UnconfirmedEmails, int TwoFactorEnabled);

    public record DashboardHealthDto(bool DatabaseHealthy, long DatabaseResponseMs);

    public record ActivityItemDto(int Id, string Type, string? TableName, DateTime DateTime, string? UserEmail);

    // RecentActivity = null όταν ο χρήστης δεν έχει AuditLogs.View
    public record DashboardDto(
        DashboardMetricsDto Metrics,
        DashboardSecurityDto Security,
        DashboardHealthDto Health,
        IReadOnlyList<ActivityItemDto>? RecentActivity);

    [ApiController]
    [Route("api/dashboard")]
    [Authorize(Policy = SystemRoles.AdminAreaPolicy)] // Χρειάζεται Admin ή έστω ένα permission
    public class DashboardController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IAuthorizationService _authorizationService;

        public DashboardController(
            ApplicationDbContext context,
            UserManager<IdentityUser> userManager,
            IAuthorizationService authorizationService)
        {
            _context = context;
            _userManager = userManager;
            _authorizationService = authorizationService;
        }

        [HttpGet]
        public async Task<DashboardDto> Get()
        {
            // Health check: πόσο γρήγορα απαντάει η βάση
            var stopwatch = Stopwatch.StartNew();
            var databaseHealthy = await _context.Database.CanConnectAsync();
            var health = new DashboardHealthDto(databaseHealthy, stopwatch.ElapsedMilliseconds);

            var now = DateTimeOffset.UtcNow;
            var since = DateTime.UtcNow.AddHours(-24);

            var metrics = new DashboardMetricsDto(
                await _context.Users.CountAsync(),
                await _context.Roles.CountAsync(),
                (await _userManager.GetUsersInRoleAsync(SystemRoles.Admin)).Count,
                await _context.AuditLogs.CountAsync(a => a.DateTime >= since));

            var security = new DashboardSecurityDto(
                await _context.Users.CountAsync(u => u.AccessFailedCount > 0),
                await _context.Users.CountAsync(u => u.LockoutEnd != null && u.LockoutEnd > now),
                await _context.Users.CountAsync(u => !u.EmailConfirmed),
                await _context.Users.CountAsync(u => u.TwoFactorEnabled));

            List<ActivityItemDto>? recentActivity = null;
            if ((await _authorizationService.AuthorizeAsync(User, Permissions.AuditLogs.View)).Succeeded)
            {
                var logs = await _context.AuditLogs
                    .OrderByDescending(a => a.DateTime)
                    .Take(6)
                    .ToListAsync();

                var userIds = logs.Where(a => a.UserId != null).Select(a => a.UserId).Distinct().ToList();
                var emails = await _context.Users
                    .Where(u => userIds.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id, u => u.Email);

                recentActivity = logs
                    .Select(l => new ActivityItemDto(
                        l.Id,
                        l.Type.ToString(),
                        l.TableName,
                        DateTime.SpecifyKind(l.DateTime, DateTimeKind.Utc),
                        l.UserId != null && emails.TryGetValue(l.UserId, out var email) ? email : null))
                    .ToList();
            }

            return new DashboardDto(metrics, security, health, recentActivity);
        }
    }
}
