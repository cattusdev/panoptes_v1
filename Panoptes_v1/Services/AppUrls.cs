namespace Panoptes_v1.Services
{
    // Η δημόσια διεύθυνση της εφαρμογής για links σε emails.
    // App:PublicBaseUrl (π.χ. https://ops.hankatt.gr): απαραίτητο για emails από jobs (χωρίς request)
    // και στην παραγωγή, ώστε ένα πλαστό Host header να μη βγάζει links σε ξένο site.
    // Χωρίς αυτό: η διεύθυνση του τρέχοντος request (development, tests).
    public class AppUrls
    {
        public const string ConfigKey = "App:PublicBaseUrl";

        private readonly string? _configured;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AppUrls(IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
        {
            var configured = configuration[ConfigKey]?.Trim().TrimEnd('/');
            _configured = string.IsNullOrEmpty(configured) ? null : configured;
            _httpContextAccessor = httpContextAccessor;
        }

        public bool IsConfigured => _configured != null;

        public string BaseUrl
        {
            get
            {
                if (_configured != null) return _configured;
                var request = _httpContextAccessor.HttpContext?.Request
                    ?? throw new InvalidOperationException($"{ConfigKey} must be set to build links outside a web request (e.g. in background jobs).");
                return $"{request.Scheme}://{request.Host}";
            }
        }

        public string Link(string path, params (string Key, string Value)[] query)
        {
            var queryString = string.Join("&", query.Select(q => $"{q.Key}={Uri.EscapeDataString(q.Value)}"));
            return queryString.Length == 0 ? $"{BaseUrl}{path}" : $"{BaseUrl}{path}?{queryString}";
        }
    }
}
