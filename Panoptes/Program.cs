using Panoptes;
using Panoptes.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Panoptes.Services;
using Panoptes.Constants;
using Panoptes.Middlewares;
using Panoptes.Services.Media;
using Panoptes.Services.Settings;
using Panoptes.Api;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Localization;
using Panoptes.Services.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Serilog;


var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString)); // Use postgres

// Identity χωρίς το έτοιμο Razor UI: όλες οι σελίδες λογαριασμού είναι στο React app
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Stores.MaxLengthForKeys = 128; // Όπως το παλιό Identity UI (ίδιο σχήμα βάσης με τα υπάρχοντα migrations)
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders()
    .AddErrorDescriber<LocalizedIdentityErrorDescriber>(); // Τα λάθη του Identity στη γλώσσα του request

// React SPA: login/denied σελίδες, και τα /api παίρνουν 401/403 αντί για redirect
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/access-denied";
    options.Events.OnRedirectToLogin = context => ApiAwareRedirect(context, StatusCodes.Status401Unauthorized);
    options.Events.OnRedirectToAccessDenied = context => ApiAwareRedirect(context, StatusCodes.Status403Forbidden);
});

// CSRF: κάθε POST/PUT/DELETE σε controller πρέπει να έχει έγκυρο antiforgery token
// (το React app στέλνει header X-XSRF-TOKEN από το cookie XSRF-TOKEN).
// AddControllersWithViews (όχι AddControllers): εκεί ζει το AutoValidateAntiforgeryToken filter. Views δεν υπάρχουν.
builder.Services.AddAntiforgery(options => options.HeaderName = "X-XSRF-TOKEN");
builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
    .AddDataAnnotationsLocalization(options =>
        options.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(SharedResource)));

// --- ΓΛΩΣΣΕΣ ---
// Μεταφράσεις του server: IStringLocalizer<SharedResource> (Resources/SharedResource.el.resx).
// Η γλώσσα κάθε request: το Accept-Language (ο client στέλνει τη γλώσσα του χρήστη), αλλιώς η προεπιλογή από το Settings → General.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddSingleton<IConfigureOptions<MvcOptions>, LocalizedMvcOptions>();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture(AppLanguages.Default);
    options.SupportedCultures = AppLanguages.Cultures;
    options.SupportedUICultures = AppLanguages.Cultures;
    options.RequestCultureProviders = [new AcceptLanguageHeaderRequestCultureProvider(), new AppDefaultCultureProvider()];
});

// Κάθε πόσο ελέγχεται το cookie απέναντι στη βάση (ώστε αλλαγές σε ρόλους/permissions να ισχύουν γρήγορα)
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.FromSeconds(
        builder.Configuration.GetValue("Security:StampValidationIntervalSeconds", 60)));

// --- DYNAMIC PERMISSION POLICIES ---
builder.Services.AddAuthorization(options =>
{
    foreach (var permissionName in Permissions.GetAll())
    {
        options.AddPolicy(permissionName, policy =>
            policy.RequireAssertion(context =>
                context.User.IsInRole(SystemRoles.Admin) ||
                context.User.HasClaim("Permission", permissionName)));
    }

    options.AddPolicy(SystemRoles.AdminAreaPolicy, policy =>
        policy.RequireAssertion(context =>
            context.User.IsInRole(SystemRoles.Admin) ||
            context.User.HasClaim(c => c.Type == "Permission")));
});

builder.Services.AddScoped<AdminGuard>();
builder.Services.AddHttpContextAccessor();
// Ρυθμίσεις από το UI (πίνακας AppSettings). Τα κλειδιά του Data Protection μένουν στο App_Data/keys,
// αλλιώς τα κρυπτογραφημένα μυστικά (π.χ. SMTP password) δεν διαβάζονται μετά από restart/redeploy.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys")))
    .SetApplicationName("Panoptes");
builder.Services.AddMemoryCache();
builder.Services.AddScoped<SettingsService>();

builder.Services.AddScoped<EmailSender>();
builder.Services.AddScoped<IEmailSender>(services => services.GetRequiredService<EmailSender>());
builder.Services.AddScoped<AccountEmails>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// --- MEDIA ---
builder.Services.AddOptions<MediaOptions>()
    .Bind(builder.Configuration.GetSection(MediaOptions.Section))
    .Validate(o => o.ChunkSize is > 0 and <= MediaOptions.MaxChunkBytes, $"Media:ChunkSize must be between 1 and {MediaOptions.MaxChunkBytes} bytes.")
    .Validate(o => o.MaxVersions >= 1 && o.MaxPixels > 0 && o.MaxFrames >= 1 && o.TrashRetentionDays >= 1, "Media limits must be positive.")
    .Validate(o => o.CleanupIntervalMinutes >= 0, "Media:CleanupIntervalMinutes can't be negative (0 turns the cleanup off).")
    .Validate(o => o.Provider is MediaOptions.FileSystemProvider or MediaOptions.S3Provider, "Media:Provider must be FileSystem or S3.")
    .Validate(o => o.Provider != MediaOptions.S3Provider || o.S3.Problem() == null,
        "Media:S3 is incomplete: ServiceUrl, AccessKey, SecretKey, two different buckets (PublicBucket, PrivateBucket) and PublicBaseUrl are required.")
    .ValidateOnStart();
builder.Services.AddSingleton<MediaPaths>();
var mediaOnS3 = builder.Configuration[$"{MediaOptions.Section}:Provider"] == MediaOptions.S3Provider;
if (mediaOnS3)
    builder.Services.AddSingleton<IMediaStorage, S3MediaStorage>(); // Cloudflare R2 / S3
else
    builder.Services.AddSingleton<IMediaStorage, FileSystemMediaStorage>();
builder.Services.AddSingleton<ImageProcessor>();
builder.Services.AddSingleton<MediaUploadLocks>();
builder.Services.AddScoped<MediaService>();
builder.Services.AddScoped<MediaUsage>();
builder.Services.AddScoped<ProfileService>();
builder.Services.AddScoped<MediaCleanup>();
builder.Services.AddHostedService<MediaCleanupService>();

// Όρια ανά χρήστη: νέα uploads (ανά λεπτό) και chunks (πιο χαλαρό, ένα μεγάλο αρχείο έχει πολλά)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var uploadsPerMinute = builder.Configuration.GetSection(MediaOptions.Section).Get<MediaOptions>()?.UploadsPerMinute ?? 120;
    options.AddPolicy(MediaRateLimits.Uploads, context => PerUser(context, uploadsPerMinute));
    options.AddPolicy(MediaRateLimits.Chunks, context => PerUser(context, uploadsPerMinute * 20));
});

var app = builder.Build();

// dotnet run -- media:migrate : αντιγραφή των τοπικών αρχείων στο S3/R2 και έξοδος (χωρίς web server)
if (args.Contains(MediaMigration.Command))
{
    Environment.ExitCode = await MediaMigration.RunAsync(app.Services);
    return;
}

// dotnet run -- db:migrate | admin:create <email> : εγκατάσταση νέας βάσης και του πρώτου Admin
if (SetupCommands.TryGet(args, out var setupCommand))
{
    Environment.ExitCode = await SetupCommands.RunAsync(setupCommand, args, app.Services);
    return;
}

// libvips φορτώνει στην εκκίνηση (αν λείπει κάτι, το μαθαίνουμε αμέσως και όχι στο πρώτο upload)
app.Services.GetRequiredService<ImageProcessor>();
var mediaPaths = app.Services.GetRequiredService<MediaPaths>();
mediaPaths.EnsureCreated();

app.UseSerilogRequestLogging();

// Configure the HTTP request pipeline.
// Όλα τα λάθη τα χειρίζεται ο GlobalExceptionHandler (JSON για το API, απλή σελίδα για τα υπόλοιπα)
app.UseExceptionHandler();

if (!app.Environment.IsDevelopment())
{
    // Στην παραγωγή θέλουμε και το HSTS (έξτρα ασφάλεια για HTTPS)
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Δημόσια αρχεία media: κατευθείαν από τον δίσκο, χωρίς βάση, με cache για πάντα (το URL έχει την έκδοση).
// Με S3/R2 τα σερβίρει το public bucket (Media:S3:PublicBaseUrl).
if (!mediaOnS3)
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(mediaPaths.Public),
        RequestPath = MediaKeys.PublicRequestPath,
        ContentTypeProvider = MediaDelivery.ContentTypes,
        OnPrepareResponse = context => MediaDelivery.ApplyHeaders(context.Context.Response, context.File.Name, MediaDelivery.PublicCache),
    });
}

app.UseRouting();
app.UseRequestLocalization();

app.UseAuthentication();
app.UseAuthorization(); //  Permissions
app.UseRateLimiter();

app.MapControllers();

// Παλιά URLs των Razor σελίδων λογαριασμού -> οι React σελίδες (bookmarks, παλιά emails)
var legacyAccountPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["Login"] = "/login",
    ["Register"] = "/register",
    ["ForgotPassword"] = "/forgot-password",
    ["ResendEmailConfirmation"] = "/resend-confirmation",
    ["Lockout"] = "/lockout",
    ["AccessDenied"] = "/access-denied",
    ["Manage"] = "/account",
};
app.MapGet("/Identity/Account/{**path}", (string? path) =>
{
    var first = (path ?? "").Split('/')[0];
    return Results.Redirect(legacyAccountPaths.TryGetValue(first, out var target) ? target : "/login");
});
app.MapGet("/Home/Privacy", () => Results.Redirect("/privacy"));

// Μόνο για τα E2E tests: ελέγχει ότι ένα crash φτάνει σωστά στον χρήστη (JSON / σελίδα λάθους)
if (app.Configuration.GetValue<bool>("Diagnostics:EnableTestEndpoints"))
{
    app.MapGet("/api/test/crash", () => { throw new InvalidOperationException("Simulated crash (API)"); });
    app.MapGet("/test/crash", () => { throw new InvalidOperationException("Simulated crash (page)"); });
    app.MapPost("/api/test/media-options", (bool? allowSvg, int? maxVersions, IOptions<MediaOptions> options) =>
    {
        if (allowSvg is bool svg) options.Value.AllowSvg = svg;
        if (maxVersions is int versions) options.Value.MaxVersions = versions;
    });
    app.MapPost("/api/test/media-cleanup", (MediaCleanup cleanup, CancellationToken cancellationToken) => cleanup.RunAsync(cancellationToken));
}

// Άγνωστα /api/* -> 404 (όχι το index.html του SPA)
app.Map("/api/{**rest}", context =>
{
    context.Response.StatusCode = StatusCodes.Status404NotFound;
    return Task.CompletedTask;
});

// Άγνωστα αρχεία media -> 404
app.Map("/media/{**rest}", context =>
{
    context.Response.StatusCode = StatusCodes.Status404NotFound;
    return Task.CompletedTask;
});

// Όλα τα υπόλοιπα (/login, /admin, ...) τα χειρίζεται το React app
app.MapFallbackToFile("spa/index.html");

app.Run();

static Task ApiAwareRedirect(Microsoft.AspNetCore.Authentication.RedirectContext<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions> context, int apiStatusCode)
{
    if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/media"))
        context.Response.StatusCode = apiStatusCode;
    else
        context.Response.Redirect(context.RedirectUri);
    return Task.CompletedTask;
}

static RateLimitPartition<string> PerUser(HttpContext context, int permitsPerMinute) =>
    RateLimitPartition.GetSlidingWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permitsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0,
        });
