using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Panoptes_v1.Services.Settings;

namespace Panoptes_v1.Services.Localization
{
    // Οι γλώσσες της εφαρμογής (ίδιες με του client: src/i18n). Τα αγγλικά είναι η "πηγή" και το fallback.
    public static class AppLanguages
    {
        public const string Default = "en";
        public static readonly IReadOnlyList<string> Supported = ["en", "el"];

        public static bool IsSupported(string? language) => language != null && Supported.Contains(language);

        public static IList<CultureInfo> Cultures => Supported.Select(CultureInfo.GetCultureInfo).ToList();

        // Προσωρινά άλλη γλώσσα (π.χ. email στη γλώσσα του παραλήπτη) και επιστροφή στην προηγούμενη στο Dispose
        public static IDisposable Use(string? language)
        {
            var previous = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
            if (IsSupported(language))
            {
                var culture = CultureInfo.GetCultureInfo(language!);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
            }
            return new Restore(previous.CurrentCulture, previous.CurrentUICulture);
        }

        private sealed class Restore(CultureInfo culture, CultureInfo uiCulture) : IDisposable
        {
            public void Dispose()
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = uiCulture;
            }
        }
    }

    // Τελευταία επιλογή του RequestLocalization: η προεπιλεγμένη γλώσσα από το Settings → General
    public class AppDefaultCultureProvider : RequestCultureProvider
    {
        public override async Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
        {
            var settings = httpContext.RequestServices.GetRequiredService<SettingsService>();
            var language = (await settings.GetAsync<GeneralSettings>()).DefaultLanguage;
            return new ProviderCultureResult(AppLanguages.IsSupported(language) ? language : AppLanguages.Default);
        }
    }
}
