using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Panoptes_v1.Data;
using Panoptes_v1.Services;
using Panoptes_v1.Services.Jobs;
using Panoptes_v1.Services.Settings;

namespace Panoptes_v1.Tests;

// Βάση των domain tests: οι πραγματικές υπηρεσίες πάνω στην πραγματική βάση, χωρίς HTTP και browser.
// Κάθε test παίρνει δικό του service provider και ρολόι (Clock) που το ορίζει το ίδιο.
public abstract class DomainTest
{
    private ServiceProvider _provider = null!;

    protected TestClock Clock { get; private set; } = null!;

    [SetUp]
    public void BuildServices()
    {
        Clock = new TestClock(DateTimeOffset.UtcNow);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddMemoryCache();
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(TestDatabase.ConnectionString));
        services.AddIdentityCore<IdentityUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        services.AddSingleton<TimeProvider>(Clock);
        services.AddScoped<SettingsService>();
        services.AddScoped<BusinessClock>();
        services.AddScoped<ReferenceGenerator>();

        // Οι δουλειές τρέχουν μόνο όταν τις ζητά το test, και ξαναδοκιμάζονται αμέσως
        services.AddOptions<JobOptions>().Configure(options =>
        {
            options.PollSeconds = 0;
            options.RetryDelaySeconds = 0;
        });
        services.AddScoped<JobQueue>();
        services.AddScoped<JobProcessor>();
        services.AddScoped<IJobHandler, TestJob>();

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [TearDown]
    public async Task DisposeServicesAsync()
    {
        await _provider.DisposeAsync();
    }

    protected AsyncServiceScope Scope() => _provider.CreateAsyncScope();

    // Ένα σύντομο "unit of work": δικό του scope, άρα καθαρό DbContext
    protected async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Scope();
        return await action(scope.ServiceProvider);
    }

    protected Task WithScopeAsync(Func<IServiceProvider, Task> action) =>
        WithScopeAsync(async services => { await action(services); return true; });

    protected Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> action) =>
        WithScopeAsync(services => action(services.GetRequiredService<ApplicationDbContext>()));

    // Σαν να τρέχει ένα request του χρήστη (για το audit log και τα Created*/Updated*). Το TraceId ξεχωρίζει
    // τις γραμμές του audit log αυτού του test. Σύγχρονο σκόπιμα: η αλλαγή του AsyncLocal φτάνει στον caller.
    protected string SignIn(string userId)
    {
        var traceId = $"test-{Guid.NewGuid():N}";
        _provider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test")),
            TraceIdentifier = traceId,
        };
        return traceId;
    }

    protected void SignOut() => _provider.GetRequiredService<IHttpContextAccessor>().HttpContext = null;

    protected static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid().ToString("N")[..8]}";
}

// Ρολόι που ορίζει το test (BusinessClock, ReferenceGenerator)
public sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
