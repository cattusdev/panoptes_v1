using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Panoptes_v1.E2E;

// Τρέχει μία φορά για όλα τα tests: reset βάσης, εκκίνηση της πραγματικής εφαρμογής σε ελεύθερο port
[SetUpFixture]
public class AppFixture
{
    public static string BaseUrl { get; private set; } = "";
    public static string SolutionDir { get; private set; } = "";

    // Τα αρχεία media των tests (καθαρίζονται σε κάθε run)
    public static string MediaRoot { get; } = Path.Combine(Path.GetTempPath(), "Panoptes_v1.E2E", "media");
    public static string MediaTempRoot { get; } = Path.Combine(Path.GetTempPath(), "Panoptes_v1.E2E", "media-tmp");

    public static string WebProjectDir { get; private set; } = "";

    private static AppProcess? _app;

    [OneTimeSetUp]
    public async Task StartAppAsync()
    {
        WebProjectDir = FindWebProjectDir();
        TestData.Configure(WebProjectDir);

        // Πάντα φρέσκο build του React client, ώστε τα tests να τρέχουν τον τρέχοντα κώδικα
        if (Environment.GetEnvironmentVariable("E2E_SKIP_CLIENT_BUILD") != "1")
            await BuildClientAsync(Path.Combine(SolutionDir, "Panoptes_v1.Client"));

        await TestData.ResetAsync();
        Mailbox.Clear();
        foreach (var dir in new[] { MediaRoot, MediaTempRoot })
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);

        _app = await AppProcess.StartAsync(DefaultEnvironment());
        BaseUrl = _app.BaseUrl;
    }

    [OneTimeTearDown]
    public void StopApp() => _app?.Dispose();

    // Οι ρυθμίσεις της εφαρμογής στα tests (env vars)· τα S3 tests προσθέτουν τις δικές τους
    public static Dictionary<string, string> DefaultEnvironment() => new()
    {
        ["ASPNETCORE_ENVIRONMENT"] = "Development",
        ["ConnectionStrings__DefaultConnection"] = TestData.ConnectionString,
        ["Security__StampValidationIntervalSeconds"] = "0", // Έλεγχος cookie σε κάθε request
        ["Email__PickupDirectory"] = Mailbox.Directory,      // Emails σε αρχεία, όχι SMTP
        ["Diagnostics__EnableTestEndpoints"] = "true",       // /api/test/crash, /test/crash, /api/test/media-*
        ["Media__Root"] = MediaRoot,
        ["Media__TempRoot"] = MediaTempRoot,
        ["Media__MaxVersions"] = "3",                        // Για να ελέγχεται εύκολα το κλάδεμα των εκδόσεων
        ["Media__UploadsPerMinute"] = "1000",
        ["Media__CleanupIntervalMinutes"] = "0",             // Ο καθαρισμός τρέχει μόνο όταν τον ζητά ένα test
        ["Jobs__PollSeconds"] = "0",                         // Οι δουλειές τρέχουν μόνο όταν τις ζητά ένα test (/api/test/jobs/run)
        ["Jobs__RetryDelaySeconds"] = "0",                   // Επανάληψη αμέσως, για να μη περιμένουν τα tests
        ["Serilog__WriteTo__1__Args__path"] = Path.Combine(Path.GetTempPath(), "Panoptes_v1.E2E", "log-.txt"),
    };

    // dotnet run της εφαρμογής, στο ίδιο configuration με τα tests (bin/<Configuration>/net8.0)
    public static ProcessStartInfo DotnetRun(IDictionary<string, string> environment, string appArguments = "")
    {
        var configuration = new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)).Parent!.Name;
        var arguments = $"run --project \"{WebProjectDir}\" --no-build --no-launch-profile -c {configuration}";
        if (appArguments.Length > 0) arguments += $" -- {appArguments}";

        var startInfo = new ProcessStartInfo("dotnet", arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var (key, value) in environment)
            startInfo.Environment[key] = value;
        return startInfo;
    }

    private static async Task BuildClientAsync(string clientDir)
    {
        var npm = OperatingSystem.IsWindows() ? "cmd.exe" : "npm";
        var args = OperatingSystem.IsWindows() ? "/c npm run build" : "run build";
        var startInfo = new ProcessStartInfo(npm, args)
        {
            WorkingDirectory = clientDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var build = Process.Start(startInfo)!;
        var output = build.StandardOutput.ReadToEndAsync();
        var errors = build.StandardError.ReadToEndAsync();
        await build.WaitForExitAsync();

        if (build.ExitCode != 0)
            throw new InvalidOperationException($"Client build failed:\n{await output}\n{await errors}");
    }

    private static string FindWebProjectDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Panoptes_v1.sln")))
            dir = dir.Parent;

        if (dir == null)
            throw new InvalidOperationException("Could not find Panoptes_v1.sln above the test output folder.");

        SolutionDir = dir.FullName;
        return Path.Combine(dir.FullName, "Panoptes_v1");
    }

    public static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

// Η εφαρμογή σε δικό της process και ελεύθερο port, μέχρι το Dispose
public sealed class AppProcess : IDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _output = new();

    private AppProcess(Process process, string baseUrl)
    {
        _process = process;
        BaseUrl = baseUrl;
        _process.OutputDataReceived += (_, e) => { lock (_output) _output.AppendLine(e.Data); };
        _process.ErrorDataReceived += (_, e) => { lock (_output) _output.AppendLine(e.Data); };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public string BaseUrl { get; }

    public static async Task<AppProcess> StartAsync(IDictionary<string, string> environment)
    {
        var baseUrl = $"http://127.0.0.1:{AppFixture.GetFreePort()}";
        var startInfo = AppFixture.DotnetRun(new Dictionary<string, string>(environment) { ["ASPNETCORE_URLS"] = baseUrl });
        var app = new AppProcess(Process.Start(startInfo)!, baseUrl);
        try
        {
            await app.WaitUntilReadyAsync(TimeSpan.FromSeconds(60));
        }
        catch
        {
            app.Dispose();
            throw;
        }
        return app;
    }

    public void Dispose()
    {
        if (!_process.HasExited)
            _process.Kill(entireProcessTree: true);
        _process.Dispose();
    }

    private async Task WaitUntilReadyAsync(TimeSpan timeout)
    {
        using var http = new HttpClient();
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
                throw new InvalidOperationException($"The app exited during startup:\n{_output}");

            try
            {
                var response = await http.GetAsync(BaseUrl);
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException)
            {
                // Δεν ακούει ακόμα
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"The app did not start within {timeout.TotalSeconds}s:\n{_output}");
    }
}
