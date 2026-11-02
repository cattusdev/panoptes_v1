using System.ComponentModel.DataAnnotations;
using Panoptes_v1.Constants;
using Panoptes_v1.Data;
using Panoptes_v1.Models;
using Panoptes_v1.Models.Media;
using Panoptes_v1.Services.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Panoptes_v1.Api
{
    public record BrandingDto(string AppName, string? LogoUrl, string? FaviconUrl);

    public record BrandingSettingsDto(string AppName, MediaItemDto? Logo, MediaItemDto? Favicon);

    public class BrandingRequest
    {
        [Required, StringLength(50, MinimumLength = 1)]
        public string AppName { get; set; } = "";

        public Guid? LogoMediaId { get; set; }
        public Guid? FaviconMediaId { get; set; }
    }

    // Όνομα, λογότυπο και favicon της εφαρμογής. Οι εικόνες διαλέγονται από τη βιβλιοθήκη και γίνονται δημόσιες.
    [ApiController]
    [Route("api/branding")]
    [Authorize]
    public class BrandingController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly MediaService _media;
        private readonly IStringLocalizer<SharedResource> _t;

        public BrandingController(ApplicationDbContext db, MediaService media, IStringLocalizer<SharedResource> localizer)
        {
            _t = localizer;
            _db = db;
            _media = media;
        }

        // Για όλες τις σελίδες (και χωρίς login)
        [HttpGet]
        [AllowAnonymous]
        public async Task<BrandingDto> Get()
        {
            Response.Headers.CacheControl = "no-cache";
            var branding = await LoadAsync();
            var logo = await ItemAsync(branding.LogoMediaId);
            var favicon = await ItemAsync(branding.FaviconMediaId);
            return new BrandingDto(branding.AppName, DisplayUrl(logo), DisplayUrl(favicon));
        }

        [HttpGet("settings")]
        [Authorize(Policy = Permissions.Settings.View)]
        public async Task<BrandingSettingsDto> Settings()
        {
            var branding = await LoadAsync();
            return new BrandingSettingsDto(branding.AppName, await ItemDtoAsync(branding.LogoMediaId), await ItemDtoAsync(branding.FaviconMediaId));
        }

        [HttpPut]
        [Authorize(Policy = Permissions.Settings.Edit)]
        public async Task<ActionResult<BrandingSettingsDto>> Update(BrandingRequest request)
        {
            var branding = await _db.Branding.FirstOrDefaultAsync();
            if (branding == null)
            {
                branding = new BrandingSettings();
                _db.Branding.Add(branding);
            }

            var changed = new[] { request.LogoMediaId, request.FaviconMediaId }
                .Where(id => id != null && id != branding.LogoMediaId && id != branding.FaviconMediaId)
                .ToList();
            if (changed.Count > 0 && !Permissions.Has(User, Permissions.Media.View))
                return Problem(_t["You need access to the media library to choose images."], statusCode: StatusCodes.Status403Forbidden);

            // Μόνο εικόνες της βιβλιοθήκης (όχι από τον κάδο)· γίνονται δημόσιες γιατί φαίνονται σε όλους
            var images = new List<MediaItem>();
            foreach (var id in new[] { request.LogoMediaId, request.FaviconMediaId }.OfType<Guid>().Distinct())
            {
                var item = await _db.MediaItems.Include(i => i.Versions)
                    .FirstOrDefaultAsync(i => i.Id == id && i.Purpose == MediaPurpose.Library && i.DeletedAt == null);
                if (item == null)
                    return Problem(_t["The selected image no longer exists."], statusCode: StatusCodes.Status400BadRequest);
                if (item.Kind != MediaKind.Image)
                    return Problem(_t["The logo and favicon must be images."], statusCode: StatusCodes.Status400BadRequest);
                images.Add(item);
            }

            branding.AppName = request.AppName.Trim();
            branding.LogoMediaId = request.LogoMediaId;
            branding.FaviconMediaId = request.FaviconMediaId;
            branding.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            foreach (var image in images.Where(i => i.Visibility != MediaVisibility.Public))
                await _media.SetVisibilityAsync(image, MediaVisibility.Public);

            return await Settings();
        }

        private async Task<BrandingSettings> LoadAsync() =>
            await _db.Branding.AsNoTracking().FirstOrDefaultAsync() ?? new BrandingSettings();

        private async Task<(MediaItem Item, MediaVersion Version)?> ItemAsync(Guid? id)
        {
            if (id == null) return null;
            var item = await _db.MediaItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id && i.DeletedAt == null);
            if (item == null) return null;
            var version = await _db.MediaVersions.AsNoTracking().FirstAsync(v => v.Id == item.CurrentVersionId);
            return (item, version);
        }

        // Το λογότυπο σε μέγεθος οθόνης (medium WebP)· τα SVG ως έχουν
        private string? DisplayUrl((MediaItem Item, MediaVersion Version)? found)
        {
            if (found == null) return null;
            var (item, version) = found.Value;
            var variant = version.Variants.FirstOrDefault(v => v.Name == ImageProcessor.Medium)
                ?? version.Variants.First(v => v.Name == ImageProcessor.Original);
            return _media.FileUrl(item, version, variant);
        }

        private async Task<MediaItemDto?> ItemDtoAsync(Guid? id)
        {
            var found = await ItemAsync(id);
            if (found == null) return null;
            var (item, version) = found.Value;
            var ownerEmail = await _db.Users.Where(u => u.Id == item.OwnerId).Select(u => u.Email).FirstOrDefaultAsync();
            return MediaDtos.Item(item, version, ownerEmail, User, _media);
        }
    }
}
