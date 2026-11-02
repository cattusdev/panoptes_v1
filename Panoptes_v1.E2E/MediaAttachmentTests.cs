using System.Text.Json;
using Microsoft.Playwright;

namespace Panoptes_v1.E2E;

// Αρχεία συνδεδεμένα με εγγραφές: ένα αρχείο restricted project δεν φαίνεται από κανέναν δρόμο σε όποιον δεν βλέπει το project
// (μέχρι το module των projects: μόνο ο Admin), δεν γίνεται δημόσιο και δεν πάει στον κάδο ούτε σβήνεται όσο είναι συνδεδεμένο.
public class MediaAttachmentTests : MediaTest
{
    private static readonly string RestrictedProject = Guid.NewGuid().ToString();

    [Test]
    public async Task RestrictedProjectFile_IsHiddenEverywhere_FromPeopleOutsideTheProject()
    {
        await LoginAsync(TestData.MediaEditor);
        var title = Unique("contract");
        var folderId = await CreateFolderAsync(Unique("clients"));
        var upload = await UploadAsync($"{title}.png", MediaFiles.Png(), folderId);
        Assert.That(upload.Ok, Is.True);
        var thumb = upload.Url("thumb");

        var admin = await SecondUserAsync(TestData.Admin);
        await AttachAsync(admin, upload.Id, projectId: RestrictedProject);

        // Ο κάτοχος και όποιος έχει Media.View: δεν το βλέπουν πουθενά
        var viewer = await SecondUserAsync(TestData.MediaViewer);
        foreach (var page in new[] { Page, viewer })
        {
            Assert.That((await ApiAsync(page, "GET", $"/api/media/{upload.Id}")).Status, Is.EqualTo(404));
            Assert.That((await ApiAsync(page, "GET", $"/api/media/{upload.Id}/versions")).Status, Is.EqualTo(404));
            Assert.That((await page.APIRequest.GetAsync(thumb)).Status, Is.EqualTo(404));
            Assert.That(await IdsAsync(page, $"?search={Uri.EscapeDataString(title)}"), Does.Not.Contain(upload.Id));
            Assert.That(await IdsAsync(page, $"?folderId={folderId}"), Does.Not.Contain(upload.Id));
            Assert.That(await FolderCountAsync(page, folderId), Is.EqualTo(0), "folder counts don't reveal hidden files");
        }

        // Ο Admin το βλέπει
        Assert.That((await ApiAsync(admin, "GET", $"/api/media/{upload.Id}")).Status, Is.EqualTo(200));
        Assert.That((await admin.APIRequest.GetAsync(thumb)).Status, Is.EqualTo(200));
        Assert.That(await FolderCountAsync(admin, folderId), Is.EqualTo(1));

        // Αποσύνδεση: ξαναγίνεται κανονικό αρχείο της κοινής βιβλιοθήκης
        Assert.That((await ApiAsync(admin, "DELETE", $"/api/test/media-attachments?mediaItemId={upload.Id}&entityType=project&entityId={RestrictedProject}")).Status, Is.EqualTo(204));
        Assert.That((await ApiAsync(viewer, "GET", $"/api/media/{upload.Id}")).Status, Is.EqualTo(200));
        Assert.That((await viewer.APIRequest.GetAsync(thumb)).Status, Is.EqualTo(200));
    }

    [Test]
    public async Task AnotherLink_DoesNotWidenAccess_TheStrictestWins()
    {
        await LoginAsync(TestData.MediaEditor);
        var upload = await UploadAsync($"{Unique("shared")}.png", MediaFiles.Png());
        var admin = await SecondUserAsync(TestData.Admin);

        // Συνδεδεμένο με έναν πελάτη (χωρίς project) ΚΑΙ με ένα restricted project
        await AttachAsync(admin, upload.Id, entityType: "client", entityId: Guid.NewGuid().ToString());
        Assert.That((await ApiAsync("GET", $"/api/media/{upload.Id}")).Status, Is.EqualTo(200), "a client link alone keeps the library rules");
        await AttachAsync(admin, upload.Id, projectId: RestrictedProject);
        Assert.That((await ApiAsync("GET", $"/api/media/{upload.Id}")).Status, Is.EqualTo(404), "the restricted project link wins");
    }

    [Test]
    public async Task FinanceOnlyLink_RequiresThePermission()
    {
        await LoginAsync(TestData.MediaViewer);
        var admin = await SecondUserAsync(TestData.Admin);
        var upload = await UploadAsync(admin, $"{Unique("invoice")}.png", MediaFiles.Png());

        await AttachAsync(admin, upload.Id, entityType: "charge", entityId: Guid.NewGuid().ToString(), requiredPermission: "Permissions.Settings.Edit");
        Assert.That((await ApiAsync("GET", $"/api/media/{upload.Id}")).Status, Is.EqualTo(404));
        Assert.That((await ApiAsync(admin, "GET", $"/api/media/{upload.Id}")).Status, Is.EqualTo(200));
    }

    [Test]
    public async Task AttachedFile_StaysPrivate_AndCantBeTrashedOrDeleted()
    {
        await LoginAsync(TestData.Admin);
        var title = Unique("proposal");
        var upload = await UploadAsync($"{title}.png", MediaFiles.Png());
        await AttachAsync(Page, upload.Id, projectId: RestrictedProject);

        var makePublic = await ApiAsync("PUT", $"/api/media/{upload.Id}", new { title, visibility = "Public" });
        Assert.That(makePublic.Status, Is.EqualTo(409));
        Assert.That(await makePublic.TextAsync(), Does.Contain("is attached to records and must stay private"));

        var branding = await ApiAsync("PUT", "/api/branding", new { appName = "Panoptes v1.0", logoMediaId = upload.Id, faviconMediaId = (string?)null });
        Assert.That(branding.Status, Is.EqualTo(409), "can't become the (public) logo either");

        var trash = await ApiAsync("POST", "/api/media/trash", new { ids = new[] { upload.Id } });
        Assert.That(trash.Status, Is.EqualTo(409));
        Assert.That(await trash.TextAsync(), Does.Contain("is attached to records. Remove it from them first."));
        Assert.That(FilesExistOnDisk(upload.Id), Is.True);
    }

    [Test]
    public async Task PublicFiles_CantBeAttached()
    {
        await LoginAsync(TestData.Admin);
        var upload = await UploadAsync($"{Unique("banner")}.png", MediaFiles.Png(), visibility: "Public");
        var response = await ApiAsync("POST", "/api/test/media-attachments", new { mediaItemId = upload.Id, entityType = "project", entityId = RestrictedProject, projectId = RestrictedProject });
        Assert.That(response.Status, Is.EqualTo(409));
        Assert.That(await response.TextAsync(), Does.Contain("Public files can't be attached to records."));
    }

    [Test]
    public async Task UploadUndo_IsRefused_OnceTheFileIsAttached()
    {
        await LoginAsync(TestData.Admin);
        var data = MediaFiles.Png();
        var start = await ApiAsync("POST", "/api/media/uploads", new { fileName = $"{Unique("scan")}.png", size = data.LongLength, visibility = "Private", purpose = "Library" });
        var uploadId = (await start.JsonAsync())!.Value.GetProperty("id").GetString()!;
        var done = await ChunkAsync(Page, uploadId, 0, data);
        var itemId = (await done.JsonAsync())!.Value.GetProperty("id").GetString()!;

        await AttachAsync(Page, itemId, projectId: RestrictedProject);
        Assert.That((await ApiAsync("DELETE", $"/api/media/uploads/{uploadId}")).Status, Is.EqualTo(409));
        Assert.That(FilesExistOnDisk(itemId), Is.True);
    }

    [Test]
    public async Task FolderDelete_IsRefused_WhenItHoldsFilesYouCantSee()
    {
        await LoginAsync(TestData.MediaManager);
        var folderId = await CreateFolderAsync(Unique("handover"));
        var admin = await SecondUserAsync(TestData.Admin);
        var hidden = await UploadAsync(admin, $"{Unique("credentials")}.png", MediaFiles.Png(), folderId);
        await AttachAsync(admin, hidden.Id, projectId: RestrictedProject);

        var delete = await ApiAsync("DELETE", $"/api/media/folders/{folderId}?withContents=true");
        Assert.That(delete.Status, Is.EqualTo(403));
        Assert.That((await ApiAsync(admin, "GET", $"/api/media/{hidden.Id}")).Status, Is.EqualTo(200), "the hidden file is untouched");
    }

    private static async Task AttachAsync(IPage page, string mediaItemId, string entityType = "project", string? entityId = null,
        string? projectId = null, string? requiredPermission = null)
    {
        var response = await ApiAsync(page, "POST", "/api/test/media-attachments",
            new { mediaItemId, entityType, entityId = entityId ?? projectId, projectId, requiredPermission });
        Assert.That(response.Status, Is.EqualTo(200), await response.TextAsync());
    }

    private static async Task<List<string>> IdsAsync(IPage page, string query)
    {
        var response = await ApiAsync(page, "GET", $"/api/media{query}");
        Assert.That(response.Status, Is.EqualTo(200));
        return (await response.JsonAsync())!.Value.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()!).ToList();
    }

    private static async Task<int> FolderCountAsync(IPage page, string folderId)
    {
        var folders = (await (await ApiAsync(page, "GET", "/api/media/folders")).JsonAsync())!.Value;
        return folders.EnumerateArray().Single(f => f.GetProperty("id").GetString() == folderId).GetProperty("itemCount").GetInt32();
    }
}
