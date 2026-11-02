using System.Diagnostics;
using Amazon.S3;
using Amazon.S3.Model;
using Panoptes_v1.Services.Media;
using Microsoft.Playwright;

namespace Panoptes_v1.E2E;

// Η εφαρμογή με Media:Provider=S3 απέναντι σε τοπικό S3 (Adobe S3Mock σε Docker), ίδια βάση με τα υπόλοιπα tests.
// Opt-in, γιατί θέλει Docker: dotnet test Panoptes_v1.E2E --filter "Category=S3"
[Explicit, Category("S3")]
public class S3StorageTests : MediaTest
{
    private const string Image = "adobe/s3mock:3.12.0";
    private const string PublicBucket = "media-public";
    private const string PrivateBucket = "media-private";

    private static string _container = "";
    private static string _s3Url = "";
    private static AppProcess? _app;
    private static AmazonS3Client? _s3;

    private static string PublicBaseUrl => $"{_s3Url}/{PublicBucket}";
    private static AmazonS3Client S3 => _s3!;

    public override BrowserNewContextOptions ContextOptions() => new() { BaseURL = _app!.BaseUrl };

    [OneTimeSetUp]
    public async Task StartS3Async()
    {
        var port = AppFixture.GetFreePort();
        _container = $"panoptes-v1-e2e-s3-{Guid.NewGuid().ToString("N")[..8]}";
        await DockerAsync($"run -d --rm --name {_container} -p 127.0.0.1:{port}:9090 {Image}");
        _s3Url = $"http://127.0.0.1:{port}";
        await WaitForS3Async();

        _s3 = S3MediaStorage.CreateClient(S3Options());
        await _s3.PutBucketAsync(PublicBucket);
        await _s3.PutBucketAsync(PrivateBucket);

        _app = await AppProcess.StartAsync(S3Environment());
    }

    [OneTimeTearDown]
    public async Task StopS3Async()
    {
        _app?.Dispose();
        _s3?.Dispose();
        if (_container.Length > 0)
            await DockerAsync($"rm -f {_container}");
    }

    [Test]
    public async Task PublicFilesComeFromTheBucket_PrivateOnesThroughTheApp()
    {
        await LoginAsync(TestData.MediaEditor);
        var photo = await UploadAsync("café.jpg", MediaFiles.Jpeg(1200, 800), visibility: "Public");
        Assert.That(photo.Ok, Is.True, photo.Detail);
        Assert.That(photo.Url("thumb"), Does.StartWith(PublicBaseUrl + "/"));

        // Κατευθείαν από το bucket, χωρίς login, με cache για πάντα
        using var http = new HttpClient();
        var thumb = await http.GetAsync(photo.Url("thumb"));
        Assert.That((int)thumb.StatusCode, Is.EqualTo(200));
        Assert.That(thumb.Content.Headers.ContentType?.MediaType, Is.EqualTo("image/webp"));
        Assert.That(thumb.Headers.CacheControl?.ToString(), Does.Contain("immutable"));
        Assert.That(await KeysAsync(PublicBucket, photo.Id), Has.Count.EqualTo(4), "thumb, medium, large, original");
        Assert.That(await KeysAsync(PrivateBucket, photo.Id), Is.Empty);

        // Ιδιωτικό: μόνο μέσα από την εφαρμογή, μετά τον έλεγχο δικαιωμάτων
        var pdf = await UploadAsync("τιμοκατάλογος.pdf", MediaFiles.Pdf());
        Assert.That(pdf.Ok, Is.True, pdf.Detail);
        Assert.That(pdf.Url("original"), Does.StartWith("/media/"));
        Assert.That(await KeysAsync(PrivateBucket, pdf.Id), Has.Count.EqualTo(1));

        var mine = await Page.APIRequest.GetAsync(pdf.Url("original"));
        Assert.That(mine.Status, Is.EqualTo(200));
        Assert.That(mine.Headers["content-type"], Is.EqualTo("application/pdf"));
        Assert.That(await mine.BodyAsync(), Is.EqualTo(MediaFiles.Pdf()));

        var download = await Page.APIRequest.GetAsync(pdf.Url("download"));
        Assert.That(download.Headers["content-disposition"], Does.Contain("attachment").And.Contain("UTF-8''"));

        var anonymous = await AnonymousS3AppAsync();
        Assert.That((await anonymous.GetAsync(pdf.Url("original"))).Status, Is.EqualTo(404));
    }

    [Test]
    public async Task VisibilityTrashAndPurge_MoveAndDeleteObjects()
    {
        await LoginAsync(TestData.MediaEditor);
        var item = await UploadAsync("poster.png", MediaFiles.Png(400, 300));
        Assert.That(await KeysAsync(PrivateBucket, item.Id), Has.Count.EqualTo(4));

        var makePublic = await ApiAsync("PUT", $"/api/media/{item.Id}", new { title = "poster", visibility = "Public" });
        var publicUrl = (await makePublic.JsonAsync())!.Value.GetProperty("version").GetProperty("urls").GetProperty("original").GetString()!;
        Assert.That(await KeysAsync(PublicBucket, item.Id), Has.Count.EqualTo(4));
        Assert.That(await KeysAsync(PrivateBucket, item.Id), Is.Empty);
        using var http = new HttpClient();
        Assert.That((int)(await http.GetAsync(publicUrl)).StatusCode, Is.EqualTo(200));

        await ApiAsync("PUT", $"/api/media/{item.Id}", new { title = "poster", visibility = "Private" });
        Assert.That((int)(await http.GetAsync(publicUrl)).StatusCode, Is.EqualTo(404), "no longer public");
        Assert.That(await KeysAsync(PrivateBucket, item.Id), Has.Count.EqualTo(4));

        await ApiAsync("POST", "/api/media/trash", new { ids = new[] { item.Id } });
        await ApiAsync("POST", "/api/media/purge", new { ids = new[] { item.Id } });
        Assert.That(await KeysAsync(PublicBucket, item.Id), Is.Empty);
        Assert.That(await KeysAsync(PrivateBucket, item.Id), Is.Empty);
    }

    [Test]
    public async Task Cleanup_MovesMisplacedObjectsBack_AndKeepsRecentOnes()
    {
        await LoginAsync(TestData.MediaEditor);
        var item = await UploadAsync("secret.png", MediaFiles.Png());
        var key = $"{Guid.Parse(item.Id):N}/v1/original.png";

        // Μετακίνηση που διακόπηκε: το ιδιωτικό αρχείο βρίσκεται στο public bucket
        await S3.CopyObjectAsync(PrivateBucket, key, PublicBucket, key);
        await S3.DeleteObjectAsync(PrivateBucket, key);
        // Αρχείο χωρίς εγγραφή, αλλά καινούριο (ίσως upload σε εξέλιξη): μένει
        var young = $"{Guid.NewGuid():N}/v1/original.png";
        await S3.PutObjectAsync(new PutObjectRequest { BucketName = PrivateBucket, Key = young, ContentBody = "x" });

        var response = await Page.APIRequest.PostAsync("/api/test/media-cleanup");
        Assert.That(response.Status, Is.EqualTo(200), await response.TextAsync());
        Assert.That((await response.JsonAsync())!.Value.GetProperty("misplacedFiles").GetInt32(), Is.GreaterThanOrEqualTo(1));

        Assert.That(await KeysAsync(PublicBucket, item.Id), Is.Empty);
        Assert.That(await KeysAsync(PrivateBucket, item.Id), Does.Contain(key));
        Assert.That(await KeysAsync(PrivateBucket, young.Split('/')[0]), Has.Count.EqualTo(1));
        Assert.That((await Page.APIRequest.GetAsync(item.Url("original"))).Status, Is.EqualTo(200));
    }

    [Test]
    public async Task MigrateCommand_CopiesLocalFilesToTheBuckets()
    {
        // Αρχεία που ανέβηκαν στην εφαρμογή με τοπικό δίσκο
        var local = await (await Browser.NewContextAsync(new() { BaseURL = AppFixture.BaseUrl })).NewPageAsync();
        await LoginAsync(local, TestData.MediaEditor);
        var banner = await UploadAsync(local, "banner.png", MediaFiles.Png(600, 200), visibility: "Public");
        var contract = await UploadAsync(local, "contract.pdf", MediaFiles.Pdf());
        Assert.That(banner.Ok && contract.Ok, Is.True);

        var (exitCode, output) = await MigrateAsync();
        Assert.That(exitCode, Is.EqualTo(0), output);
        Assert.That(output, Does.Contain("Media migration done"));
        Assert.That(await KeysAsync(PublicBucket, banner.Id), Has.Count.EqualTo(4));
        Assert.That(await KeysAsync(PrivateBucket, contract.Id), Has.Count.EqualTo(1));

        // Η εφαρμογή με S3 τα σερβίρει
        await LoginAsync(TestData.MediaEditor);
        var bannerOnS3 = await GetJsonAsync($"/api/media/{banner.Id}");
        var bannerUrl = bannerOnS3.GetProperty("version").GetProperty("urls").GetProperty("thumb").GetString()!;
        Assert.That(bannerUrl, Does.StartWith(PublicBaseUrl));
        using var http = new HttpClient();
        Assert.That((int)(await http.GetAsync(bannerUrl)).StatusCode, Is.EqualTo(200));
        Assert.That((await Page.APIRequest.GetAsync(contract.Url("original"))).Status, Is.EqualTo(200));

        // Ξανά: τίποτα καινούριο να αντιγραφεί
        var (_, again) = await MigrateAsync();
        Assert.That(again, Does.Contain(": 0 copied"));
    }

    private static S3StorageOptions S3Options() => new()
    {
        ServiceUrl = _s3Url,
        AccessKey = "test",
        SecretKey = "test",
        PublicBucket = PublicBucket,
        PrivateBucket = PrivateBucket,
        PublicBaseUrl = PublicBaseUrl,
    };

    private static Dictionary<string, string> S3Environment() => new(AppFixture.DefaultEnvironment())
    {
        ["Media__Provider"] = "S3",
        ["Media__TempRoot"] = Path.Combine(Path.GetTempPath(), "Panoptes_v1.E2E", "media-tmp-s3"),
        ["Media__S3__ServiceUrl"] = _s3Url,
        ["Media__S3__AccessKey"] = "test",
        ["Media__S3__SecretKey"] = "test",
        ["Media__S3__PublicBucket"] = PublicBucket,
        ["Media__S3__PrivateBucket"] = PrivateBucket,
        ["Media__S3__PublicBaseUrl"] = PublicBaseUrl,
    };

    private async Task<IAPIRequestContext> AnonymousS3AppAsync() =>
        await Playwright.APIRequest.NewContextAsync(new() { BaseURL = _app!.BaseUrl });

    // Τα αντικείμενα ενός αρχείου (prefix = το id του)
    private static async Task<List<string>> KeysAsync(string bucket, string itemId)
    {
        var prefix = Guid.TryParse(itemId, out var id) ? id.ToString("N") : itemId;
        var page = await S3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket, Prefix = prefix + "/" });
        return (page.S3Objects ?? []).Select(o => o.Key).ToList();
    }

    // Το τοπικό Media:Root (της κανονικής εφαρμογής των tests) -> buckets
    private static async Task<(int ExitCode, string Output)> MigrateAsync()
    {
        var environment = S3Environment();
        environment["Media__Root"] = AppFixture.MediaRoot;
        using var process = Process.Start(AppFixture.DotnetRun(environment, MediaMigration.Command))!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output + await errors);
    }

    private static async Task WaitForS3Async()
    {
        using var http = new HttpClient();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await http.GetAsync(_s3Url);
                return;
            }
            catch (HttpRequestException)
            {
                await Task.Delay(500);
            }
        }
        throw new TimeoutException($"S3Mock did not start at {_s3Url}.");
    }

    private static async Task DockerAsync(string arguments)
    {
        var startInfo = new ProcessStartInfo("docker", arguments) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Docker isn't available. The S3 tests need Docker.");
        var errors = process.StandardError.ReadToEndAsync();
        await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"docker {arguments} failed: {await errors}");
    }
}
