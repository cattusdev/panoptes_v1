using System.Text.Json;
using Panoptes.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Panoptes.E2E;

// Ιστορικό εκδόσεων και κάδος από το UI
public class MediaVersionsAndTrashUiTests : MediaTest
{
    [Test]
    public async Task VersionHistory_RestoreAndDeleteVersions()
    {
        await LoginAsync(TestData.MediaEditor);
        var title = Unique("brochure");
        var v1 = await UploadAsync($"{title}.png", MediaFiles.Png(300, 200));
        Assert.That((await UploadAsync("brochure-2.png", MediaFiles.Png(320, 200), replaceItemId: v1.Id)).Ok, Is.True);
        Assert.That((await UploadAsync("brochure-3.png", MediaFiles.Png(340, 200), replaceItemId: v1.Id)).Ok, Is.True);

        await Page.GotoAsync($"/admin/media?q={Uri.EscapeDataString(title)}");
        await Page.GetByRole(AriaRole.Button, new() { Name = $"Open {title}", Exact = true }).ClickAsync();
        var sheet = Page.GetByRole(AriaRole.Dialog);
        await sheet.GetByRole(AriaRole.Tab, new() { Name = "Versions" }).ClickAsync();

        var versions = sheet.GetByRole(AriaRole.List, new() { Name = "Versions" }).GetByRole(AriaRole.Listitem);
        await Expect(versions).ToHaveCountAsync(3);
        await Expect(versions.First).ToHaveAccessibleNameAsync("Version 3");
        await Expect(versions.First).ToContainTextAsync("Current");
        await Expect(sheet).ToContainTextAsync("Up to 3 versions are kept");

        // Επαναφορά της 1ης: γίνεται τρέχουσα, η καρτέλα μένει ανοιχτή
        var first = sheet.GetByRole(AriaRole.Listitem, new() { Name = "Version 1", Exact = true });
        await first.GetByRole(AriaRole.Button, new() { Name = "Restore" }).ClickAsync();
        await ExpectToastAsync("Restored version 1");
        await Expect(first).ToContainTextAsync("Current");
        await Expect(sheet.GetByRole(AriaRole.Tab, new() { Name = "Versions" })).ToHaveAttributeAsync("aria-selected", "true");
        var item = await GetJsonAsync($"/api/media/{v1.Id}");
        Assert.That(item.GetProperty("version").GetProperty("number").GetInt32(), Is.EqualTo(1));
        Assert.That(item.GetProperty("version").GetProperty("width").GetInt32(), Is.EqualTo(300));

        // Η τρέχουσα δεν διαγράφεται· μια παλιά ναι (με επιβεβαίωση)
        await first.GetByRole(AriaRole.Button, new() { Name = "Actions for version 1" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Menuitem, new() { Name = "Delete version" })).ToHaveCountAsync(0);
        await Page.Keyboard.PressAsync("Escape");

        var second = sheet.GetByRole(AriaRole.Listitem, new() { Name = "Version 2", Exact = true });
        await second.GetByRole(AriaRole.Button, new() { Name = "Actions for version 2" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Delete version" }).ClickAsync();
        await Page.GetByRole(AriaRole.Alertdialog).GetByRole(AriaRole.Button, new() { Name = "Delete version" }).ClickAsync();
        await ExpectToastAsync("Version 2 deleted");
        await Expect(versions).ToHaveCountAsync(2);
        Assert.That((await GetJsonAsync($"/api/media/{v1.Id}/versions")).GetArrayLength(), Is.EqualTo(2));
    }

    [Test]
    public async Task EmptyTrash_DeletesYourFiles_AndKeepsOtherPeoples()
    {
        var other = await SecondUserAsync(TestData.MediaEditor);
        var theirs = await UploadAsync(other, $"{Unique("theirs")}.png", MediaFiles.Png());
        Assert.That((await ApiAsync(other, "POST", "/api/media/trash", new { ids = new[] { theirs.Id } })).Status, Is.EqualTo(200));

        await LoginAsync(TestData.MediaEditor2);
        var mine = await UploadAsync($"{Unique("mine")}.png", MediaFiles.Png());
        Assert.That((await ApiAsync("POST", "/api/media/trash", new { ids = new[] { mine.Id } })).Status, Is.EqualTo(200));

        await Page.GotoAsync("/admin/media?view=trash");
        await Expect(Page.GetByText("Files in the trash are deleted forever after 30 days.")).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Empty trash" }).ClickAsync();
        var confirm = Page.GetByRole(AriaRole.Alertdialog);
        await Expect(confirm).ToContainTextAsync("Other people's files stay");
        await confirm.GetByRole(AriaRole.Button, new() { Name = "Empty trash" }).ClickAsync();
        await ExpectToastAsync("deleted forever");

        Assert.That(FilesExistOnDisk(mine.Id), Is.False, "my file is gone");
        Assert.That(FilesExistOnDisk(theirs.Id), Is.True, "someone else's file stays in the trash");
        Assert.That((await ApiAsync(other, "GET", $"/api/media/{theirs.Id}")).Status, Is.EqualTo(200));
    }
}

// Ο καθαρισμός στο παρασκήνιο (τρέχει με το /api/test/media-cleanup· η "παλιότητα" γράφεται στη βάση)
public class MediaCleanupTests : MediaTest
{
    private async Task<JsonElement> RunCleanupAsync()
    {
        var response = await Page.APIRequest.PostAsync("/api/test/media-cleanup");
        Assert.That(response.Status, Is.EqualTo(200), await response.TextAsync());
        return (await response.JsonAsync())!.Value;
    }

    private static Task AgeAsync(Func<ApplicationDbContext, Task> update) =>
        TestData.WithServicesAsync(async services =>
        {
            await update(services.GetRequiredService<ApplicationDbContext>());
            return true;
        });

    [Test]
    public async Task ExpiredTrash_IsDeletedForever_RecentTrashStays()
    {
        await LoginAsync(TestData.MediaEditor);
        var old = await UploadAsync("old.png", MediaFiles.Png());
        var recent = await UploadAsync("recent.png", MediaFiles.Png());
        await ApiAsync("POST", "/api/media/trash", new { ids = new[] { old.Id, recent.Id } });

        var oldId = Guid.Parse(old.Id);
        await AgeAsync(db => db.MediaItems.Where(i => i.Id == oldId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.DeletedAt, DateTime.UtcNow.AddDays(-31))));

        var result = await RunCleanupAsync();
        Assert.That(result.GetProperty("trashedItems").GetInt32(), Is.GreaterThanOrEqualTo(1));
        Assert.That((await ApiAsync("GET", $"/api/media/{old.Id}")).Status, Is.EqualTo(404));
        Assert.That(FilesExistOnDisk(old.Id), Is.False);
        Assert.That((await ApiAsync("GET", $"/api/media/{recent.Id}")).Status, Is.EqualTo(200));
        Assert.That(FilesExistOnDisk(recent.Id), Is.True);
    }

    [Test]
    public async Task StaleUploads_AndLeftoverTempFiles_AreRemoved()
    {
        await LoginAsync(TestData.MediaEditor);
        var data = MediaFiles.Pdf();
        var start = await ApiAsync("POST", "/api/media/uploads", new { fileName = "half.pdf", size = data.LongLength + 10, visibility = "Private" });
        var uploadId = (await start.JsonAsync())!.Value.GetProperty("id").GetString()!;
        Assert.That((await ChunkAsync(Page, uploadId, 0, data)).Status, Is.EqualTo(204));
        var partFile = Path.Combine(AppFixture.MediaTempRoot, $"{Guid.Parse(uploadId):N}.part");
        Assert.That(File.Exists(partFile), Is.True);

        var id = Guid.Parse(uploadId);
        await AgeAsync(db => db.MediaUploads.Where(u => u.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.CreatedAt, DateTime.UtcNow.AddHours(-25))));

        // Αρχεία χωρίς session: το παλιό φεύγει, το καινούριο μένει (μπορεί να είναι upload σε εξέλιξη)
        var leftover = Path.Combine(AppFixture.MediaTempRoot, $"{Guid.NewGuid():N}.part");
        var fresh = Path.Combine(AppFixture.MediaTempRoot, $"{Guid.NewGuid():N}.part");
        await File.WriteAllBytesAsync(leftover, [1, 2, 3]);
        await File.WriteAllBytesAsync(fresh, [1, 2, 3]);
        File.SetLastWriteTimeUtc(leftover, DateTime.UtcNow.AddDays(-2));

        await RunCleanupAsync();
        Assert.That(File.Exists(partFile), Is.False);
        Assert.That(File.Exists(leftover), Is.False);
        Assert.That(File.Exists(fresh), Is.True);
        File.Delete(fresh);

        var resume = await Page.APIRequest.FetchAsync($"/api/media/uploads/{uploadId}", new() { Method = "HEAD" });
        Assert.That(resume.Status, Is.EqualTo(404), "the session is gone");
    }

    [Test]
    public async Task OrphanFilesAreDeleted_AndPrivateFilesNeverStayPublic()
    {
        await LoginAsync(TestData.MediaEditor);

        // Αρχείο χωρίς εγγραφή στη βάση (π.χ. crash στη μέση): φεύγει όταν είναι αρκετά παλιό
        var orphanDir = Path.Combine(AppFixture.MediaRoot, "private", Guid.NewGuid().ToString("N"), "v1");
        Directory.CreateDirectory(orphanDir);
        var orphan = Path.Combine(orphanDir, "original.png");
        var young = Path.Combine(orphanDir, "thumb.webp");
        await File.WriteAllBytesAsync(orphan, MediaFiles.Png());
        await File.WriteAllBytesAsync(young, MediaFiles.Png());
        File.SetLastWriteTimeUtc(orphan, DateTime.UtcNow.AddHours(-2));

        // Ιδιωτικό αρχείο που έμεινε στη δημόσια περιοχή (π.χ. διακοπή στη μετακίνηση)
        var item = await UploadAsync("secret.png", MediaFiles.Png());
        var key = $"{Guid.Parse(item.Id):N}/v1/original.png";
        var privatePath = Path.Combine(AppFixture.MediaRoot, "private", key.Replace('/', Path.DirectorySeparatorChar));
        var publicPath = Path.Combine(AppFixture.MediaRoot, "public", key.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(publicPath)!);
        File.Move(privatePath, publicPath);
        var anonymous = await AnonymousAsync();
        Assert.That((await anonymous.GetAsync($"/media/p/{key}")).Status, Is.EqualTo(200), "exposed before the cleanup");

        var result = await RunCleanupAsync();
        Assert.That(result.GetProperty("orphanFiles").GetInt32(), Is.GreaterThanOrEqualTo(1));
        Assert.That(result.GetProperty("misplacedFiles").GetInt32(), Is.GreaterThanOrEqualTo(1));
        Assert.That(File.Exists(orphan), Is.False);
        Assert.That(File.Exists(young), Is.True, "recent files may belong to an upload in progress");
        Directory.Delete(Path.GetDirectoryName(orphanDir)!, recursive: true);

        Assert.That(File.Exists(privatePath), Is.True);
        Assert.That(File.Exists(publicPath), Is.False);
        Assert.That((await anonymous.GetAsync($"/media/p/{key}")).Status, Is.EqualTo(404));
        Assert.That((await Page.APIRequest.GetAsync(item.Url("original"))).Status, Is.EqualTo(200));
    }

    [Test]
    public async Task UnusedProfilePhotos_AreDeleted()
    {
        await LoginAsync(TestData.AvatarUser2);
        var used = await UploadAsync("me.png", MediaFiles.Png(), purpose: "Avatar");
        Assert.That((await ApiAsync("PUT", "/api/account/avatar", new { mediaId = used.Id })).Status, Is.EqualTo(200));
        var abandoned = await UploadAsync("draft.png", MediaFiles.Png(), purpose: "Avatar");
        var fresh = await UploadAsync("now.png", MediaFiles.Png(), purpose: "Avatar");

        var ids = new[] { Guid.Parse(used.Id), Guid.Parse(abandoned.Id) };
        await AgeAsync(db => db.MediaItems.Where(i => ids.Contains(i.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.CreatedAt, DateTime.UtcNow.AddDays(-2))));

        await RunCleanupAsync();
        Assert.That(FilesExistOnDisk(used.Id), Is.True, "the photo in use stays");
        Assert.That(FilesExistOnDisk(abandoned.Id), Is.False);
        Assert.That(FilesExistOnDisk(fresh.Id), Is.True, "may still be on its way to the profile");

        var remaining = await TestData.WithServicesAsync(services =>
            services.GetRequiredService<ApplicationDbContext>().MediaItems.CountAsync(i => i.Id == ids[1]));
        Assert.That(remaining, Is.Zero);
        await ApiAsync("DELETE", "/api/account/avatar");
    }

    [Test]
    public async Task LoweringTheVersionLimit_PrunesOldVersions()
    {
        await LoginAsync(TestData.MediaEditor);
        await Page.APIRequest.PostAsync("/api/test/media-options?maxVersions=5");
        string itemId;
        try
        {
            var v1 = await UploadAsync("logo.png", MediaFiles.Png());
            itemId = v1.Id;
            for (var i = 2; i <= 5; i++)
                Assert.That((await UploadAsync($"logo-{i}.png", MediaFiles.Png(), replaceItemId: itemId)).Ok, Is.True);
            Assert.That((await GetJsonAsync($"/api/media/{itemId}/versions")).GetArrayLength(), Is.EqualTo(5));
        }
        finally
        {
            await Page.APIRequest.PostAsync("/api/test/media-options?maxVersions=3");
        }

        var result = await RunCleanupAsync();
        Assert.That(result.GetProperty("versions").GetInt32(), Is.GreaterThanOrEqualTo(2));
        var numbers = (await GetJsonAsync($"/api/media/{itemId}/versions")).EnumerateArray().Select(v => v.GetProperty("number").GetInt32());
        Assert.That(numbers, Is.EqualTo(new[] { 5, 4, 3 }));
        Assert.That(Directory.Exists(Path.Combine(AppFixture.MediaRoot, "private", Guid.Parse(itemId).ToString("N"), "v1")), Is.False);
    }
}
