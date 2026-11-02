using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Panoptes_v1.Data;
using Panoptes_v1.Models;

namespace Panoptes_v1.Services.Settings
{
    // Μια ενότητα ρυθμίσεων = μία γραμμή στον πίνακα AppSettings (Key = η ενότητα, Value = JSON)
    public interface ISettingsSection
    {
        static abstract string Key { get; }
    }

    // Ρυθμίσεις της εφαρμογής που αλλάζουν από το UI. Νέα ενότητα = νέα κλάση ISettingsSection, χωρίς migration.
    // Οι τιμές κρατιούνται στη μνήμη και ανανεώνονται σε κάθε αποθήκευση. Τα μυστικά κρυπτογραφούνται με Data Protection.
    public class SettingsService
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);
        private const string ProtectorPurpose = "Panoptes_v1.Settings.v1";

        private readonly ApplicationDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly IDataProtector _protector;

        public SettingsService(ApplicationDbContext db, IMemoryCache cache, IDataProtectionProvider dataProtection)
        {
            _db = db;
            _cache = cache;
            _protector = dataProtection.CreateProtector(ProtectorPurpose);
        }

        // Χωρίς γραμμή στη βάση: οι προεπιλογές της κλάσης
        public async Task<T> GetAsync<T>() where T : class, ISettingsSection, new()
        {
            var row = await LoadAsync<T>();
            return row == null ? new T() : JsonSerializer.Deserialize<T>(row.Value, Json) ?? new T();
        }

        public async Task SaveAsync<T>(T value) where T : class, ISettingsSection, new()
        {
            var row = await TrackedRowAsync<T>();
            row.Value = JsonSerializer.Serialize(value, Json);
            row.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            _cache.Remove(CacheKey<T>());
        }

        public async Task<bool> HasSecretAsync<T>() where T : class, ISettingsSection, new() =>
            !string.IsNullOrEmpty((await LoadAsync<T>())?.Secret);

        public async Task<string?> GetSecretAsync<T>() where T : class, ISettingsSection, new()
        {
            var secret = (await LoadAsync<T>())?.Secret;
            if (string.IsNullOrEmpty(secret)) return null;
            try
            {
                return _protector.Unprotect(secret);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // Χάθηκαν τα κλειδιά (π.χ. νέο μηχάνημα χωρίς App_Data/keys): σαν να μην υπάρχει, ξαναδίνεται από το UI
                return null;
            }
        }

        // null ή κενό = διαγραφή του μυστικού
        public async Task SetSecretAsync<T>(string? plain) where T : class, ISettingsSection, new()
        {
            var row = await TrackedRowAsync<T>();
            row.Secret = string.IsNullOrEmpty(plain) ? null : _protector.Protect(plain);
            row.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            _cache.Remove(CacheKey<T>());
        }

        private static string CacheKey<T>() where T : ISettingsSection => $"settings:{T.Key}";

        private Task<AppSetting?> LoadAsync<T>() where T : class, ISettingsSection, new() =>
            _cache.GetOrCreateAsync(CacheKey<T>(), entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheFor;
                var key = T.Key;
                return _db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key);
            });

        private async Task<AppSetting> TrackedRowAsync<T>() where T : class, ISettingsSection, new()
        {
            var key = T.Key;
            var row = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
            if (row == null)
            {
                row = new AppSetting { Key = key, Value = JsonSerializer.Serialize(new T(), Json) };
                _db.AppSettings.Add(row);
            }
            return row;
        }
    }
}
