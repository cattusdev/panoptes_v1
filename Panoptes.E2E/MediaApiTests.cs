using System.Text.Json;
using System.Text.RegularExpressions;
using Panoptes.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Panoptes.E2E;

// Βοηθητικά για τα media tests: upload με το πρωτόκολλο των chunks, όπως το κάνει ο client
public abstract class MediaTest : E2ETest
{
    protected const int Mb = 1024 * 1024;

    protected record UploadResult(IAPIResponse Response, JsonElement Json)
    {
        public bool Ok => Response.Status == 200;
        public string Id => Json.GetProperty("id").GetString()!;
        public JsonElement Version => Json.GetProperty("version");
        public string Url(string variant) => Version.GetProperty("urls").GetProperty(variant).GetString()!;
        public string? Detail => Json.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
    }

    protected Task<UploadResult> UploadAsync(string fileName, byte[] data, string? folderId = null, string visibility = "Private",
        string? replaceItemId = null, int chunkSize = Mb, string purpose = "Library") =>
        UploadAsync(Page, fileName, data, folderId, visibility, replaceItemId, chunkSize, purpose);

    protected static async Task<UploadResult> UploadAsync(IPage page, string fileName, byte[] data, string? folderId = null,
        string visibility = "Private", string? replaceItemId = null, int chunkSize = Mb, string purpose = "Library")
    {
        var start = await ApiAsync(page, "POST", "/api/media/uploads", new { fileName, size = data.LongLength, folderId, visibility, replaceItemId, purpose });
        if (start.Status != 200) return new UploadResult(start, await JsonOf(start));
        var id = (await start.JsonAsync())!.Value.GetProperty("id").GetString()!;

        IAPIResponse last = start;
        for (var offset = 0; offset < data.Length; offset += chunkSize)
        {
            last = await ChunkAsync(page, id, offset, data.AsSpan(offset, Math.Min(chunkSize, data.Length - offset)).ToArray());
            if (last.Status is not (200 or 204)) break;
        }
        return new UploadResult(last, await JsonOf(last));
    }

    protected static async Task<IAPIResponse> ChunkAsync(IPage page, string uploadId, long offset, byte[] chunk)
    {
        var headers = await CsrfHeadersAsync(page);
        headers["Upload-Offset"] = offset.ToString();
        headers["Content-Type"] = "application/offset+octet-stream";
        return await page.APIRequest.FetchAsync($"/api/media/uploads/{uploadId}", new() { Method = "PATCH", DataByte = chunk, Headers = headers });
    }

    protected async Task<JsonElement> GetJsonAsync(string url)
    {
        var response = await ApiAsync("GET", url);
        Assert.That(response.Status, Is.EqualTo(200), $"GET {url}");
        return (await response.JsonAsync())!.Value;
    }

    protected async Task<List<string>> ListIdsAsync(string query)
    {
        var page = await GetJsonAsync($"/api/media{query}");
        return page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()!).ToList();
    }

    protected Task<string> CreateFolderAsync(string name, string? parentId = null) => CreateFolderAsync(Page, name, parentId);

    protected static async Task<string> CreateFolderAsync(IPage page, string name, string? parentId = null)
    {
        var response = await ApiAsync(page, "POST", "/api/media/folders", new { name, parentId });
        Assert.That(response.Status, Is.EqualTo(200), await response.TextAsync());
        return (await response.JsonAsync())!.Value.GetProperty("id").GetString()!;
    }

    protected Task<IAPIRequestContext> AnonymousAsync() =>
        Playwright.APIRequest.NewContextAsync(new() { BaseURL = AppFixture.BaseUrl });

    protected async Task<IPage> SecondUserAsync(string email)
    {
        var context = await Browser.NewContextAsync(ContextOptions());
        var page = await context.NewPageAsync();
        await LoginAsync(page, email);
        return page;
    }

    protected static string Unique(string name) => $"{name} {Guid.NewGuid().ToString("N")[..6]}";

    // Ο φάκελος ενός αρχείου στον δίσκο (για ελέγχους διαγραφής)
    protected static bool FilesExistOnDisk(string itemId)
    {
        var folder = Guid.Parse(itemId).ToString("N");
        return Directory.Exists(Path.Combine(AppFixture.MediaRoot, "private", folder)) ||
               Directory.Exists(Path.Combine(AppFixture.MediaRoot, "public", folder));
    }

    private static async Task<JsonElement> JsonOf(IAPIResponse response)
    {
        var text = await response.TextAsync();
        if (string.IsNullOrWhiteSpace(text)) return default;
        try { return JsonDocument.Parse(text).RootElement.Clone(); }
        catch (JsonException) { return default; }
    }
}

public class MediaUploadTests : MediaTest
{
    [Test]
    public async Task Photo_IsRotated_Resized_AndStrippedOfMetadata()
    {
        var photo = MediaFiles.PhotoWithMetadata();
        Assert.That(MediaFiles.HasMetadata(photo), Is.True, "the test photo must carry metadata");

        await LoginAsync(TestData.MediaEditor);
        var upload = await UploadAsync("holiday.jpg", photo);
        Assert.That(upload.Ok, Is.True, upload.Detail);

        Assert.That(upload.Json.GetProperty("kind").GetString(), Is.EqualTo("Image"));
        Assert.That(upload.Json.GetProperty("title").GetString(), Is.EqualTo("holiday"));
        Assert.That(upload.Version.GetProperty("width").GetInt32(), Is.EqualTo(400), "EXIF orientation 6 = portrait");
        Assert.That(upload.Version.GetProperty("height").GetInt32(), Is.EqualTo(600));
        Assert.That(upload.Version.GetProperty("dominantColor").GetString(), Does.Match("^#[0-9a-f]{6}$"));

        var expected = new Dictionary<string, (int, int)>
        {
            ["original"] = (400, 600),
            ["thumb"] = (320, 320),
            ["medium"] = (400, 600), // Ποτέ μεγέθυνση
            ["large"] = (400, 600),
        };
        foreach (var (variant, size) in expected)
        {
            var response = await Page.APIRequest.GetAsync(upload.Url(variant));
            Assert.That(response.Status, Is.EqualTo(200), variant);
            var bytes = await response.BodyAsync();
            var (width, height, _) = MediaFiles.Measure(bytes);
            Assert.That((width, height), Is.EqualTo(size), variant);
            Assert.That(MediaFiles.HasMetadata(bytes), Is.False, $"{variant} must not keep metadata");
            Assert.That(System.Text.Encoding.Latin1.GetString(bytes), Does.Not.Contain(MediaFiles.SecretMarker), variant);
            Assert.That(response.Headers["x-content-type-options"], Is.EqualTo("nosniff"));
            Assert.That(response.Headers["cache-control"], Does.Not.Contain("public"), "private files are never cached by shared caches");
            Assert.That(response.Headers["content-type"], Is.EqualTo(variant == "original" ? "image/jpeg" : "image/webp"));
        }
    }

    [Test]
    public async Task AnimatedGif_StaysAnimated_ThumbnailIsStill()
    {
        await LoginAsync(TestData.MediaEditor);
        var upload = await UploadAsync("spinner.gif", MediaFiles.AnimatedGif(frames: 3));
        Assert.That(upload.Ok, Is.True, upload.Detail);

        Assert.That(MediaFiles.Measure(await (await Page.APIRequest.GetAsync(upload.Url("original"))).BodyAsync()).Pages, Is.EqualTo(3));
        Assert.That(MediaFiles.Measure(await (await Page.APIRequest.GetAsync(upload.Url("medium"))).BodyAsync()).Pages, Is.EqualTo(3));
        Assert.That(MediaFiles.Measure(await (await Page.APIRequest.GetAsync(upload.Url("thumb"))).BodyAsync()).Pages, Is.EqualTo(1));
    }

    [Test]
    public async Task GreekFileName_IsKept_AndDownloadsWithItsName()
    {
        await LoginAsync(TestData.MediaEditor);
        var upload = await UploadAsync("Φωτογραφία παραλίας.png", MediaFiles.Png());
        Assert.That(upload.Ok, Is.True, upload.Detail);
        Assert.That(upload.Json.GetProperty("title").GetString(), Is.EqualTo("Φωτογραφία παραλίας"));
        Assert.That(upload.Version.GetProperty("fileName").GetString(), Is.EqualTo("Φωτογραφία παραλίας.png"));

        var download = await Page.APIRequest.GetAsync(upload.Url("download"));
        Assert.That(download.Status, Is.EqualTo(200));
        Assert.That(download.Headers["content-disposition"], Does.StartWith("attachment"));
        Assert.That(download.Headers["content-disposition"], Does.Contain("filename*=UTF-8''" + Uri.EscapeDataString("Φωτογραφία παραλίας.png")));
    }

    [Test]
    public async Task UnsafeOrInvalidFiles_AreRejected_WithClearMessages()
    {
        await LoginAsync(TestData.MediaEditor);
        var cases = new (string Name, byte[] Data, string Message)[]
        {
            ("photo.jpg", MediaFiles.Text("<html><script>alert(1)</script></html>"), "doesn't match its .jpg extension"),
            ("picture.gif", MediaFiles.Png(), "doesn't match its .gif extension"),
            ("huge.png", MediaFiles.HugePng(), "The image is too large (8,000 × 8,000)"),
            ("frames.gif", MediaFiles.AnimatedGif(frames: 301, width: 8, height: 8), "too many frames"),
            ("bomb.zip", MediaFiles.ZipBomb(), "expands to an unsafe size"),
            ("locked.zip", MediaFiles.EncryptedZip(), "Password-protected archives"),
            ("evil.zip", MediaFiles.ZipWith(("../../escape.txt", "x")), "unsafe file paths"),
            ("macro.docx", MediaFiles.Docx(withMacros: true), "macros"),
            ("fake.docx", MediaFiles.ZipWith(("hello.txt", "hi")), "doesn't match its .docx extension"),
            ("binary.txt", [0x48, 0x00, 0x49], "doesn't match its .txt extension"),
            ("report.docm", MediaFiles.Docx(), "macros aren't allowed"),
            ("photo.heic", [1, 2, 3], "HEIC photos aren't supported"),
            ("setup.exe", [0x4D, 0x5A], "(.exe) isn't allowed"),
            ("old.doc", [1, 2, 3], "Old Office formats"),
        };

        foreach (var (name, data, message) in cases)
        {
            var upload = await UploadAsync(name, data);
            Assert.That(upload.Response.Status, Is.AnyOf(400, 422), name);
            Assert.That(upload.Detail, Does.Contain(message), name);
        }

        // Πολύ μεγάλο αρχείο: απορρίπτεται πριν σταλεί οποιοδήποτε byte
        var start = await ApiAsync("POST", "/api/media/uploads", new { fileName = "big.jpg", size = 21L * Mb });
        Assert.That(start.Status, Is.EqualTo(400));
        Assert.That(await start.TextAsync(), Does.Contain("Images can be up to 20 MB"));

        // Τίποτα δεν μπήκε στη βιβλιοθήκη
        Assert.That(await ListIdsAsync("?search=frames"), Is.Empty);
        Assert.That(await ListIdsAsync("?search=bomb"), Is.Empty);
    }

    [Test]
    public async Task Documents_AreStoredAsIs_AndServedSafely()
    {
        await LoginAsync(TestData.MediaEditor);

        var docx = MediaFiles.Docx();
        var upload = await UploadAsync("contract.docx", docx);
        Assert.That(upload.Ok, Is.True, upload.Detail);
        Assert.That(upload.Json.GetProperty("kind").GetString(), Is.EqualTo("Document"));
        var file = await Page.APIRequest.GetAsync(upload.Url("original"));
        Assert.That(await file.BodyAsync(), Is.EqualTo(docx));
        Assert.That(file.Headers["content-disposition"], Does.StartWith("attachment"), "Office files always download");

        var pdf = await UploadAsync("manual.pdf", MediaFiles.Pdf());
        Assert.That(pdf.Ok, Is.True, pdf.Detail);
        var pdfFile = await Page.APIRequest.GetAsync(pdf.Url("original"));
        Assert.That(pdfFile.Headers["content-type"], Is.EqualTo("application/pdf"));
        Assert.That(pdfFile.Headers.ContainsKey("content-disposition"), Is.False, "PDFs open in the browser");

        var csv = await UploadAsync("export.csv", MediaFiles.Text("name,city\nΜαρία,Αθήνα\n"));
        Assert.That(csv.Ok, Is.True, csv.Detail);
        var csvFile = await Page.APIRequest.GetAsync(csv.Url("original"));
        Assert.That(csvFile.Headers["content-type"], Is.EqualTo("text/plain; charset=utf-8"));

        var zip = await UploadAsync("assets.zip", MediaFiles.ZipWith(("readme.txt", "hello")));
        Assert.That(zip.Ok, Is.True, zip.Detail);
        Assert.That(zip.Json.GetProperty("kind").GetString(), Is.EqualTo("Archive"));
        Assert.That(zip.Version.GetProperty("urls").GetProperty("thumb").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public async Task Svg_IsBlockedByDefault_AndSanitizedWhenAllowed()
    {
        await LoginAsync(TestData.MediaEditor);
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" onload="alert(1)" viewBox="0 0 10 10">
              <script>alert('xss')</script>
              <rect width="10" height="10" fill="#7c3aed"/>
              <foreignObject><div xmlns="http://www.w3.org/1999/xhtml">hi</div></foreignObject>
              <a xlink:href="javascript:alert(1)"><circle r="2"/></a>
              <use href="https://evil.example/sprite.svg#x"/>
            </svg>
            """;

        var blocked = await UploadAsync("logo.svg", MediaFiles.Text(svg));
        Assert.That(blocked.Response.Status, Is.EqualTo(400));
        Assert.That(blocked.Detail, Does.Contain("SVG files aren't allowed"));

        await Page.APIRequest.PostAsync("/api/test/media-options?allowSvg=true");
        try
        {
            var upload = await UploadAsync("logo.svg", MediaFiles.Text(svg));
            Assert.That(upload.Ok, Is.True, upload.Detail);

            var file = await Page.APIRequest.GetAsync(upload.Url("original"));
            var content = await file.TextAsync();
            Assert.That(content, Does.Contain("<rect"));
            Assert.That(content, Does.Not.Contain("script").And.Not.Contain("onload").And.Not.Contain("foreignObject")
                .And.Not.Contain("javascript:").And.Not.Contain("evil.example"));
            Assert.That(file.Headers["content-security-policy"], Does.Contain("sandbox"));
        }
        finally
        {
            await Page.APIRequest.PostAsync("/api/test/media-options?allowSvg=false");
        }
    }

    [Test]
    public async Task ChunkedUpload_Resumes_AndRetriesAreHarmless()
    {
        await LoginAsync(TestData.MediaEditor);
        var data = MediaFiles.Pdf(12 * Mb);
        const int chunk = 5 * Mb;

        var start = await ApiAsync("POST", "/api/media/uploads", new { fileName = "big-manual.pdf", size = data.LongLength });
        Assert.That(start.Status, Is.EqualTo(200));
        var id = (await start.JsonAsync())!.Value.GetProperty("id").GetString()!;
        byte[] Part(int index) => data.AsSpan(index * chunk, Math.Min(chunk, data.Length - index * chunk)).ToArray();

        var first = await ChunkAsync(Page, id, 0, Part(0));
        Assert.That(first.Status, Is.EqualTo(204));
        Assert.That(first.Headers["upload-offset"], Is.EqualTo(chunk.ToString()));

        var retry = await ChunkAsync(Page, id, 0, Part(0));
        Assert.That(retry.Status, Is.EqualTo(204), "a repeated chunk is accepted without writing it twice");
        Assert.That(retry.Headers["upload-offset"], Is.EqualTo(chunk.ToString()));

        var head = await Page.APIRequest.FetchAsync($"/api/media/uploads/{id}", new() { Method = "HEAD" });
        Assert.That(head.Headers["upload-offset"], Is.EqualTo(chunk.ToString()));

        var outOfOrder = await ChunkAsync(Page, id, 2L * chunk, Part(2));
        Assert.That(outOfOrder.Status, Is.EqualTo(409));
        Assert.That(outOfOrder.Headers["upload-offset"], Is.EqualTo(chunk.ToString()));

        Assert.That((await ChunkAsync(Page, id, chunk, Part(1))).Status, Is.EqualTo(204));
        var last = await ChunkAsync(Page, id, 2L * chunk, Part(2));
        Assert.That(last.Status, Is.EqualTo(200));
        var item = (await last.JsonAsync())!.Value;
        Assert.That(item.GetProperty("version").GetProperty("sizeBytes").GetInt64(), Is.EqualTo(data.LongLength));

        // Ξανά το τελευταίο (π.χ. χάθηκε η απάντηση): ίδιο αρχείο, όχι διπλό
        var again = await ChunkAsync(Page, id, 2L * chunk, Part(2));
        Assert.That(again.Status, Is.EqualTo(200));
        Assert.That((await again.JsonAsync())!.Value.GetProperty("id").GetString(), Is.EqualTo(item.GetProperty("id").GetString()));

        var stored = await (await Page.APIRequest.GetAsync(item.GetProperty("version").GetProperty("urls").GetProperty("original").GetString()!)).BodyAsync();
        Assert.That(stored, Is.EqualTo(data));
    }

    [Test]
    public async Task UploadSession_BelongsToTheUserWhoStartedIt()
    {
        await LoginAsync(TestData.MediaEditor);
        var start = await ApiAsync("POST", "/api/media/uploads", new { fileName = "mine.png", size = 10 });
        var id = (await start.JsonAsync())!.Value.GetProperty("id").GetString()!;

        var other = await SecondUserAsync(TestData.MediaManager);
        Assert.That((await ChunkAsync(other, id, 0, new byte[10])).Status, Is.EqualTo(404));
        Assert.That((await other.APIRequest.FetchAsync($"/api/media/uploads/{id}", new() { Method = "HEAD" })).Status, Is.EqualTo(404));
        Assert.That((await ApiAsync(other, "DELETE", $"/api/media/uploads/{id}")).Status, Is.EqualTo(404));

        Assert.That((await ApiAsync("DELETE", $"/api/media/uploads/{id}")).Status, Is.EqualTo(204));
    }

    [Test]
    public async Task Revert_RemovesTheJustUploadedFile()
    {
        await LoginAsync(TestData.MediaEditor);
        var start = await ApiAsync("POST", "/api/media/uploads", new { fileName = "oops.png", size = MediaFiles.Png().LongLength });
        var uploadId = (await start.JsonAsync())!.Value.GetProperty("id").GetString()!;
        var done = await ChunkAsync(Page, uploadId, 0, MediaFiles.Png());
        var itemId = (await done.JsonAsync())!.Value.GetProperty("id").GetString()!;
        Assert.That(FilesExistOnDisk(itemId), Is.True);

        Assert.That((await ApiAsync("DELETE", $"/api/media/uploads/{uploadId}")).Status, Is.EqualTo(204));
        Assert.That((await ApiAsync("GET", $"/api/media/{itemId}")).Status, Is.EqualTo(404));
        Assert.That(FilesExistOnDisk(itemId), Is.False);
    }
}

public class MediaPermissionTests : MediaTest
{
    [Test]
    public async Task Viewer_CanBrowse_ButNotUpload()
    {
        await LoginAsync(TestData.MediaViewer);
        Assert.That((await ApiAsync("GET", "/api/media")).Status, Is.EqualTo(200));

        var upload = await UploadAsync("nope.png", MediaFiles.Png());
        Assert.That(upload.Response.Status, Is.EqualTo(403));
        Assert.That((await ApiAsync("POST", "/api/media/folders", new { name = Unique("Nope") })).Status, Is.EqualTo(403));
    }

    [Test]
    public async Task UserWithoutMediaPermissions_CannotBrowse()
    {
        await LoginAsync(TestData.NoAccess);
        Assert.That((await ApiAsync("GET", "/api/media")).Status, Is.EqualTo(403));
        Assert.That((await ApiAsync("GET", "/api/media/folders")).Status, Is.EqualTo(403));
    }

    [Test]
    public async Task Editor_CannotChangeSomeoneElsesFile_ButManagerCan()
    {
        var owner = await SecondUserAsync(TestData.MediaEditor2);
        var upload = await UploadAsync(owner, "theirs.png", MediaFiles.Png());
        Assert.That(upload.Ok, Is.True, upload.Detail);

        await LoginAsync(TestData.MediaEditor);
        var item = await GetJsonAsync($"/api/media/{upload.Id}");
        Assert.That(item.GetProperty("canEdit").GetBoolean(), Is.False);
        Assert.That(item.GetProperty("canDelete").GetBoolean(), Is.False);

        Assert.That((await ApiAsync("PUT", $"/api/media/{upload.Id}", new { title = "Mine now", visibility = "Public" })).Status, Is.EqualTo(403));
        Assert.That((await ApiAsync("POST", "/api/media/move", new { ids = new[] { upload.Id }, folderId = (string?)null })).Status, Is.EqualTo(403));
        Assert.That((await ApiAsync("POST", "/api/media/trash", new { ids = new[] { upload.Id } })).Status, Is.EqualTo(403));
        var replace = await UploadAsync("replacement.png", MediaFiles.Png(), replaceItemId: upload.Id);
        Assert.That(replace.Response.Status, Is.EqualTo(403));

        var manager = await SecondUserAsync(TestData.MediaManager);
        Assert.That((await ApiAsync(manager, "POST", "/api/media/trash", new { ids = new[] { upload.Id } })).Status, Is.EqualTo(200));
    }

    [Test]
    public async Task PrivateFiles_AreHidden_PublicFilesAreServedToEveryone()
    {
        await LoginAsync(TestData.MediaEditor);
        var upload = await UploadAsync("team-photo.png", MediaFiles.Png());
        var privateThumb = upload.Url("thumb");
        Assert.That(privateThumb, Does.StartWith($"/media/{Guid.Parse(upload.Id):N}/1/"));

        var anonymous = await AnonymousAsync();
        Assert.That((await anonymous.GetAsync(privateThumb)).Status, Is.EqualTo(404), "no hint that the file exists");

        var noAccess = await SecondUserAsync(TestData.NoAccess);
        Assert.That((await noAccess.APIRequest.GetAsync(privateThumb)).Status, Is.EqualTo(404));
        var viewer = await SecondUserAsync(TestData.MediaViewer);
        Assert.That((await viewer.APIRequest.GetAsync(privateThumb)).Status, Is.EqualTo(200));

        // Δημόσιο: σερβίρεται απευθείας (static), με cache για πάντα
        var update = await ApiAsync("PUT", $"/api/media/{upload.Id}", new { title = "Team photo", altText = "The team", visibility = "Public" });
        Assert.That(update.Status, Is.EqualTo(200), await update.TextAsync());
        var publicThumb = (await update.JsonAsync())!.Value.GetProperty("version").GetProperty("urls").GetProperty("thumb").GetString()!;
        Assert.That(publicThumb, Does.StartWith("/media/p/"));

        var served = await anonymous.GetAsync(publicThumb);
        Assert.That(served.Status, Is.EqualTo(200));
        Assert.That(served.Headers["cache-control"], Does.Contain("immutable"));
        Assert.That(served.Headers["x-content-type-options"], Is.EqualTo("nosniff"));

        // Στον κάδο: τίποτα δημόσιο
        Assert.That((await ApiAsync("POST", "/api/media/trash", new { ids = new[] { upload.Id } })).Status, Is.EqualTo(200));
        Assert.That((await anonymous.GetAsync(publicThumb)).Status, Is.EqualTo(404));
        Assert.That((await viewer.APIRequest.GetAsync(privateThumb)).Status, Is.EqualTo(200), "trash stays visible to the library");
    }
}

public class MediaFolderTests : MediaTest
{
    [Test]
    public async Task Folders_HaveUniqueNames_AndCannotContainThemselves()
    {
        await LoginAsync(TestData.MediaEditor);
        var photosName = Unique("Photos");
        var photos = await CreateFolderAsync(photosName);

        var duplicate = await ApiAsync("POST", "/api/media/folders", new { name = photosName.ToUpperInvariant() });
        Assert.That(duplicate.Status, Is.EqualTo(409), "names are unique per parent, case-insensitive (also at the root)");

        var docs = await CreateFolderAsync(Unique("Docs"));
        await CreateFolderAsync(photosName, parentId: docs); // Ίδιο όνομα σε άλλον γονέα: εντάξει

        Assert.That((await ApiAsync("POST", "/api/media/folders", new { name = "a/b" })).Status, Is.EqualTo(400));

        var renamed = await ApiAsync("PUT", $"/api/media/folders/{docs}", new { name = Unique("Documents") });
        Assert.That(renamed.Status, Is.EqualTo(200));

        Assert.That((await ApiAsync("POST", $"/api/media/folders/{docs}/move", new { parentId = photos })).Status, Is.EqualTo(200));
        var cycle = await ApiAsync("POST", $"/api/media/folders/{photos}/move", new { parentId = docs });
        Assert.That(cycle.Status, Is.EqualTo(400));
        Assert.That(await cycle.TextAsync(), Does.Contain("into itself"));
    }

    [Test]
    public async Task Files_MoveBetweenFolders_AndSearchLooksEverywhere()
    {
        await LoginAsync(TestData.MediaEditor);
        var folder = await CreateFolderAsync(Unique("Campaign"));
        var title = Unique("banner");
        var upload = await UploadAsync($"{title}.png", MediaFiles.Png(), folderId: folder);
        Assert.That(upload.Ok, Is.True, upload.Detail);

        Assert.That(await ListIdsAsync($"?folderId={folder}"), Does.Contain(upload.Id));
        Assert.That(await ListIdsAsync(""), Does.Not.Contain(upload.Id), "the root lists only root files");
        Assert.That(await ListIdsAsync($"?search={Uri.EscapeDataString(title)}"), Is.EqualTo(new[] { upload.Id }));

        var move = await ApiAsync("POST", "/api/media/move", new { ids = new[] { upload.Id }, folderId = (string?)null });
        Assert.That(move.Status, Is.EqualTo(200));
        Assert.That(await ListIdsAsync(""), Does.Contain(upload.Id));
    }

    [Test]
    public async Task Paging_UsesACursor_WithoutDuplicates()
    {
        await LoginAsync(TestData.MediaEditor);
        var folder = await CreateFolderAsync(Unique("Paging"));
        var uploaded = new List<string>();
        foreach (var name in new[] { "e", "c", "a", "d", "b" })
            uploaded.Add((await UploadAsync($"{name}.png", MediaFiles.Png(), folderId: folder)).Id);

        async Task<List<string>> AllPages(string sort)
        {
            var ids = new List<string>();
            string? cursor = null;
            do
            {
                var page = await GetJsonAsync($"/api/media?folderId={folder}&sort={sort}&limit=2{(cursor == null ? "" : $"&cursor={cursor}")}");
                ids.AddRange(page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()!));
                cursor = page.GetProperty("nextCursor").GetString();
            } while (cursor != null);
            return ids;
        }

        Assert.That(await AllPages("newest"), Is.EqualTo(Enumerable.Reverse(uploaded).ToList()));
        Assert.That(await AllPages("oldest"), Is.EqualTo(uploaded));
        Assert.That(await AllPages("name"), Is.EqualTo(new[] { uploaded[2], uploaded[4], uploaded[1], uploaded[3], uploaded[0] }));
    }

    [Test]
    public async Task DeletingAFolderWithContents_MovesFilesToTrash()
    {
        await LoginAsync(TestData.MediaEditor);
        var parent = await CreateFolderAsync(Unique("Old project"));
        var child = await CreateFolderAsync("Assets", parentId: parent);
        var upload = await UploadAsync("logo.png", MediaFiles.Png(), folderId: child);

        var notEmpty = await ApiAsync("DELETE", $"/api/media/folders/{parent}");
        Assert.That(notEmpty.Status, Is.EqualTo(409));

        var summary = await GetJsonAsync($"/api/media/folders/{parent}/summary");
        Assert.That(summary.GetProperty("files").GetInt32(), Is.EqualTo(1));
        Assert.That(summary.GetProperty("folders").GetInt32(), Is.EqualTo(1));

        Assert.That((await ApiAsync("DELETE", $"/api/media/folders/{parent}?withContents=true")).Status, Is.EqualTo(204));
        var folders = await GetJsonAsync("/api/media/folders");
        Assert.That(folders.EnumerateArray().Select(f => f.GetProperty("id").GetString()), Does.Not.Contain(parent).And.Not.Contain(child));
        Assert.That(await ListIdsAsync("?trash=true"), Does.Contain(upload.Id));

        // Ο φάκελος δεν υπάρχει πια: επιστρέφει στη ρίζα
        Assert.That((await ApiAsync("POST", "/api/media/restore", new { ids = new[] { upload.Id } })).Status, Is.EqualTo(200));
        var restored = await GetJsonAsync($"/api/media/{upload.Id}");
        Assert.That(restored.GetProperty("folderId").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(restored.GetProperty("deletedAt").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public async Task Trash_RestoresToTheOriginalFolder_AndPurgeRemovesFiles()
    {
        await LoginAsync(TestData.MediaEditor);
        var folder = await CreateFolderAsync(Unique("Reports"));
        var upload = await UploadAsync("q3.pdf", MediaFiles.Pdf(), folderId: folder);

        Assert.That((await ApiAsync("POST", "/api/media/trash", new { ids = new[] { upload.Id } })).Status, Is.EqualTo(200));
        Assert.That(await ListIdsAsync($"?folderId={folder}"), Does.Not.Contain(upload.Id));
        Assert.That(await ListIdsAsync("?trash=true"), Does.Contain(upload.Id));

        Assert.That((await ApiAsync("POST", "/api/media/restore", new { ids = new[] { upload.Id } })).Status, Is.EqualTo(200));
        Assert.That(await ListIdsAsync($"?folderId={folder}"), Does.Contain(upload.Id));

        // Οριστική διαγραφή: μόνο από τον κάδο, και τότε φεύγουν και τα αρχεία
        var notTrashed = await ApiAsync("POST", "/api/media/purge", new { ids = new[] { upload.Id } });
        Assert.That((await notTrashed.JsonAsync())!.Value.GetProperty("deleted").GetInt32(), Is.EqualTo(0));
        Assert.That(FilesExistOnDisk(upload.Id), Is.True);

        await ApiAsync("POST", "/api/media/trash", new { ids = new[] { upload.Id } });
        Assert.That((await ApiAsync("POST", "/api/media/purge", new { ids = new[] { upload.Id } })).Status, Is.EqualTo(200));
        Assert.That((await ApiAsync("GET", $"/api/media/{upload.Id}")).Status, Is.EqualTo(404));
        Assert.That(FilesExistOnDisk(upload.Id), Is.False);
    }

    [Test]
    public async Task UploadSessions_AreNotWrittenToTheAuditLog()
    {
        await LoginAsync(TestData.MediaEditor);
        var upload = await UploadAsync("audited.png", MediaFiles.Png());
        Assert.That(upload.Ok, Is.True);

        var (sessions, items) = await TestData.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            return (await db.AuditLogs.CountAsync(l => l.TableName == "MediaUploads"),
                    await db.AuditLogs.CountAsync(l => l.TableName == "MediaItems" && l.PrimaryKey!.Contains(upload.Id)));
        });
        Assert.That(sessions, Is.EqualTo(0));
        Assert.That(items, Is.EqualTo(1), "the new file itself is audited");
    }
}

public class MediaVersionTests : MediaTest
{
    [Test]
    public async Task ReplacingAFile_AddsVersions_RestoresAndKeepsTheLatest()
    {
        await LoginAsync(TestData.MediaEditor);
        var v1 = await UploadAsync("hero.png", MediaFiles.Png(100, 50));
        Assert.That(v1.Ok, Is.True, v1.Detail);

        var v2 = await UploadAsync("hero-new.jpg", MediaFiles.Jpeg(200, 100), replaceItemId: v1.Id);
        Assert.That(v2.Ok, Is.True, v2.Detail);
        Assert.That(v2.Id, Is.EqualTo(v1.Id), "same item, new version");
        Assert.That(v2.Version.GetProperty("number").GetInt32(), Is.EqualTo(2));
        Assert.That(v2.Version.GetProperty("extension").GetString(), Is.EqualTo("jpg"));
        Assert.That(v2.Url("thumb"), Does.Contain("/2/"), "new URL per version (no stale caches)");

        var versions = await GetJsonAsync($"/api/media/{v1.Id}/versions");
        var firstId = versions.EnumerateArray().Single(v => v.GetProperty("number").GetInt32() == 1).GetProperty("id").GetString();
        var restored = await ApiAsync("POST", $"/api/media/{v1.Id}/versions/{firstId}/restore");
        Assert.That((await restored.JsonAsync())!.Value.GetProperty("version").GetProperty("number").GetInt32(), Is.EqualTo(1));

        var deleteCurrent = await ApiAsync("DELETE", $"/api/media/{v1.Id}/versions/{firstId}");
        Assert.That(deleteCurrent.Status, Is.EqualTo(400));

        // MaxVersions = 3 στα tests: κρατιούνται οι 3 πιο πρόσφατες
        for (var i = 0; i < 3; i++)
            Assert.That((await UploadAsync($"hero-{i}.png", MediaFiles.Png(), replaceItemId: v1.Id)).Ok, Is.True);

        var numbers = (await GetJsonAsync($"/api/media/{v1.Id}/versions")).EnumerateArray().Select(v => v.GetProperty("number").GetInt32());
        Assert.That(numbers, Is.EqualTo(new[] { 5, 4, 3 }));
    }

    [Test]
    public async Task AFileCanOnlyBeReplacedWithTheSameKind()
    {
        await LoginAsync(TestData.MediaEditor);
        var image = await UploadAsync("chart.png", MediaFiles.Png());
        var replace = await UploadAsync("chart.pdf", MediaFiles.Pdf(), replaceItemId: image.Id);
        Assert.That(replace.Response.Status, Is.EqualTo(422));
        Assert.That(replace.Detail, Is.EqualTo("Image can only be replaced with another image."));

        var versions = await GetJsonAsync($"/api/media/{image.Id}/versions");
        Assert.That(versions.GetArrayLength(), Is.EqualTo(1));
    }
}
