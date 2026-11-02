using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Panoptes.E2E;

// Η σελίδα /admin/media (UI): upload με FilePond, φάκελοι, λεπτομέρειες, μαζικές ενέργειες, κάδος
public class MediaLibraryTests : MediaTest
{
    private ILocator FilePondInput => Page.Locator(".filepond--browser");
    private ILocator Dialog => Page.GetByRole(AriaRole.Dialog);
    private ILocator Card(string title) => Page.GetByRole(AriaRole.Button, new() { Name = $"Open {title}", Exact = true });

    private async Task OpenFolderPageAsync(string folderId)
    {
        await Page.GotoAsync($"/admin/media?folder={folderId}");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Media" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Upload_AddsFilesToTheCurrentFolder()
    {
        await LoginAsync(TestData.MediaEditor);
        var folderName = Unique("Posters");
        var folder = await CreateFolderAsync(folderName);
        await OpenFolderPageAsync(folder);
        await Expect(Page.GetByText("This folder is empty")).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        await Expect(Dialog).ToContainTextAsync(folderName);
        await FilePondInput.SetInputFilesAsync(new FilePayload { Name = "poster.png", MimeType = "image/png", Buffer = MediaFiles.Png() });

        await Expect(Page.Locator(".filepond--item")).ToHaveAttributeAsync("data-filepond-item-state", "processing-complete");
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Done (1 uploaded)" }).ClickAsync();
        await Expect(Card("poster")).ToBeVisibleAsync();

        // Το κλείσιμο του dialog δεν αναιρεί τα ολοκληρωμένα uploads (το FilePond καλεί abort στο destroy)
        await Page.WaitForTimeoutAsync(500);
        await Page.ReloadAsync();
        await Expect(Card("poster")).ToBeVisibleAsync();
        Assert.That(await ListIdsAsync($"?folderId={folder}"), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task RejectedFile_ShowsTheServerMessageOnTheFile()
    {
        await LoginAsync(TestData.MediaEditor);
        await OpenFolderPageAsync(await CreateFolderAsync(Unique("Rejects")));

        await Page.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        await FilePondInput.SetInputFilesAsync(new FilePayload
        {
            Name = "fake.docx",
            MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            Buffer = MediaFiles.ZipWith(("hello.txt", "not a document")),
        });

        var item = Page.Locator(".filepond--item");
        await Expect(item).ToHaveAttributeAsync("data-filepond-item-state", "processing-error");
        await Expect(item).ToContainTextAsync("doesn't match its .docx extension");
    }

    [Test]
    public async Task DroppingFilesOnThePage_OpensTheUploader()
    {
        await LoginAsync(TestData.MediaEditor);
        await OpenFolderPageAsync(await CreateFolderAsync(Unique("Dropped")));

        // Drag & drop αρχείου από τον υπολογιστή (DataTransfer με File)
        var dataTransfer = await Page.EvaluateHandleAsync("""
            () => {
              const data = new DataTransfer()
              data.items.add(new File(['hello from a drop'], 'notes.txt', { type: 'text/plain' }))
              return data
            }
            """);
        await Page.GetByRole(AriaRole.Heading, new() { Name = "Media" }).DispatchEventAsync("dragenter", new { dataTransfer });
        var overlay = Page.GetByTestId("drop-overlay");
        await Expect(overlay).ToContainTextAsync("Drop files to upload");
        await overlay.DispatchEventAsync("drop", new { dataTransfer });

        await Expect(Page.Locator(".filepond--item")).ToHaveAttributeAsync("data-filepond-item-state", "processing-complete");
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Done (1 uploaded)" }).ClickAsync();
        await Expect(Card("notes")).ToBeVisibleAsync();
    }

    [Test]
    public async Task DraggingOverTheUploadDialog_DoesNotOpenThePageOverlay_AndDropsAnywhereInIt()
    {
        await LoginAsync(TestData.MediaEditor);
        await OpenFolderPageAsync(await CreateFolderAsync(Unique("Dialog drop")));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".filepond--root")).ToBeVisibleAsync();

        var dataTransfer = await Page.EvaluateHandleAsync("""
            () => {
              const data = new DataTransfer()
              data.items.add(new File(['dropped next to the box'], 'beside.txt', { type: 'text/plain' }))
              return data
            }
            """);
        // Το dialog είναι σε portal: το σύρσιμο πάνω του δεν πρέπει να ανοίγει το overlay της σελίδας (που μετά δεν έκλεινε)
        var title = Dialog.GetByRole(AriaRole.Heading, new() { Name = "Upload files" });
        await title.DispatchEventAsync("dragenter", new { dataTransfer });
        await title.DispatchEventAsync("dragover", new { dataTransfer });
        await Expect(Page.GetByTestId("drop-overlay")).ToHaveCountAsync(0);

        // Αφημένο έξω από το κουτί του FilePond αλλά μέσα στο dialog: ανεβαίνει κανονικά
        await title.DispatchEventAsync("drop", new { dataTransfer });
        await Expect(Page.Locator(".filepond--item")).ToHaveAttributeAsync("data-filepond-item-state", "processing-complete");
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Done (1 uploaded)" }).ClickAsync();
        await Expect(Card("beside")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Details_EditAltText_AndMakeThePhotoPublic()
    {
        await LoginAsync(TestData.MediaEditor);
        var folder = await CreateFolderAsync(Unique("Team"));
        var upload = await UploadAsync("team.png", MediaFiles.Png(), folderId: folder);
        await OpenFolderPageAsync(folder);

        await Card("team").ClickAsync();
        var sheet = Page.GetByRole(AriaRole.Dialog);
        await Expect(sheet.GetByRole(AriaRole.Heading, new() { Name = "team" })).ToBeVisibleAsync();
        await Expect(sheet).ToContainTextAsync("Private links only work for signed-in people");

        await sheet.GetByLabel("Alt text").FillAsync("The whole team at the office");
        await sheet.GetByRole(AriaRole.Switch, new() { Name = "Public link" }).ClickAsync();
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await ExpectToastAsync("Changes saved");
        await Expect(sheet).ToContainTextAsync("These links work for anyone.");

        var item = await GetJsonAsync($"/api/media/{upload.Id}");
        Assert.That(item.GetProperty("visibility").GetString(), Is.EqualTo("Public"));
        Assert.That(item.GetProperty("altText").GetString(), Is.EqualTo("The whole team at the office"));
    }

    [Test]
    public async Task OthersFiles_AreReadOnlyInTheDetails()
    {
        var owner = await SecondUserAsync(TestData.MediaEditor2);
        var folder = await CreateFolderAsync(owner, Unique("Shared"));
        await UploadAsync(owner, "their-logo.png", MediaFiles.Png(), folderId: folder);

        await LoginAsync(TestData.MediaEditor);
        await OpenFolderPageAsync(folder);
        await Card("their-logo").ClickAsync();

        var sheet = Page.GetByRole(AriaRole.Dialog);
        await Expect(sheet).ToContainTextAsync("Only the person who uploaded this file or a media manager can edit it.");
        await Expect(sheet.GetByRole(AriaRole.Button, new() { Name = "Save changes" })).ToHaveCountAsync(0);
        await Expect(sheet.GetByRole(AriaRole.Button, new() { Name = "Move to trash" })).ToHaveCountAsync(0);
        await Expect(sheet.GetByRole(AriaRole.Link, new() { Name = "Download" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Folders_CreateOpenAndNavigateWithBreadcrumbs()
    {
        await LoginAsync(TestData.MediaEditor);
        var parent = await CreateFolderAsync(Unique("Brand"));
        await OpenFolderPageAsync(parent);

        var childName = Unique("Logos");
        await Page.GetByRole(AriaRole.Button, new() { Name = "New folder" }).ClickAsync();
        await Page.FillAsync("#folder-name", childName);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create folder" }).ClickAsync();
        await ExpectToastAsync($"Folder “{childName}” created");

        await Page.GetByRole(AriaRole.Button, new() { Name = $"Open folder {childName}" }).ClickAsync();
        var breadcrumb = Page.GetByRole(AriaRole.Navigation, new() { Name = "breadcrumb" });
        await Expect(breadcrumb).ToContainTextAsync(childName);
        await Expect(Page).ToHaveURLAsync(new Regex("folder="));

        await breadcrumb.GetByRole(AriaRole.Button, new() { Name = "Library" }).ClickAsync();
        await Expect(breadcrumb.GetByText("Library")).ToHaveAttributeAsync("aria-current", "page");
    }

    [Test]
    public async Task BulkMove_MovesTheSelectedFiles()
    {
        await LoginAsync(TestData.MediaEditor);
        var source = await CreateFolderAsync(Unique("Inbox"));
        var targetName = Unique("Archive");
        var target = await CreateFolderAsync(targetName);
        await UploadAsync("first.png", MediaFiles.Png(), folderId: source);
        await UploadAsync("second.png", MediaFiles.Png(), folderId: source);
        await OpenFolderPageAsync(source);

        await Page.GetByRole(AriaRole.Checkbox, new() { Name = "Select first" }).ClickAsync();
        await Page.GetByRole(AriaRole.Checkbox, new() { Name = "Select second" }).ClickAsync();
        var toolbar = Page.GetByRole(AriaRole.Toolbar, new() { Name = "Selected files" });
        await Expect(toolbar).ToContainTextAsync("2 selected");

        await toolbar.GetByRole(AriaRole.Button, new() { Name = "Move" }).ClickAsync();
        await Page.GetByRole(AriaRole.Radio, new() { Name = targetName }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Move here" }).ClickAsync();
        await ExpectToastAsync($"Moved 2 files to {targetName}");

        await Expect(Page.GetByText("This folder is empty")).ToBeVisibleAsync();
        Assert.That(await ListIdsAsync($"?folderId={target}"), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Trash_DeleteThenRestore()
    {
        await LoginAsync(TestData.MediaEditor);
        var folder = await CreateFolderAsync(Unique("Drafts"));
        var upload = await UploadAsync("draft-report.pdf", MediaFiles.Pdf(), folderId: folder);
        await OpenFolderPageAsync(folder);

        await Page.GetByRole(AriaRole.Checkbox, new() { Name = "Select draft-report" }).ClickAsync();
        await Page.GetByRole(AriaRole.Toolbar, new() { Name = "Selected files" }).GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();
        await ExpectToastAsync("1 file moved to trash");
        await Expect(Card("draft-report")).ToHaveCountAsync(0);

        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Folders" }).GetByRole(AriaRole.Button, new() { Name = "Trash" }).ClickAsync();
        await Card("draft-report").ClickAsync();
        await Page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Restore" }).ClickAsync();
        await ExpectToastAsync("“draft-report” restored");

        Assert.That(await ListIdsAsync($"?folderId={folder}"), Does.Contain(upload.Id));
    }

    [Test]
    public async Task ListView_ShowsFilesInATable()
    {
        await LoginAsync(TestData.MediaEditor);
        var folder = await CreateFolderAsync(Unique("Spreadsheets"));
        await UploadAsync("budget.csv", MediaFiles.Text("a,b\n1,2\n"), folderId: folder);
        await OpenFolderPageAsync(folder);

        await Page.GetByRole(AriaRole.Radio, new() { Name = "List view" }).ClickAsync();
        await Expect(Row("budget")).ToContainTextAsync("CSV");
        await Page.ReloadAsync();
        await Expect(Row("budget")).ToBeVisibleAsync(); // Η επιλογή θυμάται
        await Page.GetByRole(AriaRole.Radio, new() { Name = "Grid view" }).ClickAsync();
    }

    [Test]
    public async Task Viewer_SeesTheLibrary_WithoutUploadOrFolderActions()
    {
        await LoginAsync(TestData.MediaViewer);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Media" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Media" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "New folder" })).ToHaveCountAsync(0);
    }

    [Test]
    public async Task UsersWithoutMediaAccess_DontSeeTheMenuItem()
    {
        await LoginAsync(TestData.Auditor);
        await Page.GotoAsync("/admin");
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Audit Logs" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Media" })).ToHaveCountAsync(0);
    }
}

// Οπτικός έλεγχος της βιβλιοθήκης (μόνο όταν ζητηθεί: --filter "Category=Screenshots")
[Explicit, Category("Screenshots")]
public class MediaScreenshotTests : MediaTest
{
    private static string OutputDir => Path.Combine(AppFixture.SolutionDir, "Panoptes.E2E", "TestResults", "screenshots");

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = AppFixture.BaseUrl,
        ViewportSize = new() { Width = 1440, Height = 900 },
    };

    [Test]
    public async Task MediaLibrary()
    {
        await LoginAsync(TestData.MediaManager);
        var folder = await CreateFolderAsync(Unique("Website"));
        await CreateFolderAsync("Hero images", parentId: folder);
        await CreateFolderAsync("Icons", parentId: folder);
        await UploadAsync("sunset.jpg", MediaFiles.Jpeg(1200, 800), folderId: folder, visibility: "Public");
        await UploadAsync("portrait.png", MediaFiles.Png(600, 900), folderId: folder);
        await UploadAsync("spinner.gif", MediaFiles.AnimatedGif(), folderId: folder);
        await UploadAsync("price-list.pdf", MediaFiles.Pdf(), folderId: folder);
        await UploadAsync("contract.docx", MediaFiles.Docx(), folderId: folder);
        await UploadAsync("sales.csv", MediaFiles.Text("a,b\n1,2\n"), folderId: folder);
        await UploadAsync("brand-assets.zip", MediaFiles.ZipWith(("readme.txt", "hi")), folderId: folder);

        await Page.GotoAsync($"/admin/media?folder={folder}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Shot("30-media-grid");

        await Page.GetByRole(AriaRole.Checkbox, new() { Name = "Select sunset" }).ClickAsync();
        await Page.GetByRole(AriaRole.Checkbox, new() { Name = "Select portrait" }).ClickAsync();
        await Shot("31-media-selection");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Clear selection" }).ClickAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Open sunset", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Heading, new() { Name = "sunset" })).ToBeVisibleAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(400); // Τέλος του animation
        await Shot("32-media-details");
        await Page.Keyboard.PressAsync("Escape");

        await Page.GetByRole(AriaRole.Radio, new() { Name = "List view" }).ClickAsync();
        await Shot("33-media-list");
        await Page.GetByRole(AriaRole.Radio, new() { Name = "Grid view" }).ClickAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        await Page.Locator(".filepond--browser").SetInputFilesAsync(new[]
        {
            new FilePayload { Name = "new-banner.png", MimeType = "image/png", Buffer = MediaFiles.Png(800, 300) },
            new FilePayload { Name = "fake.docx", MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document", Buffer = MediaFiles.ZipWith(("a.txt", "x")) },
        });
        foreach (var item in await Page.Locator(".filepond--item").AllAsync())
            await Expect(item).ToHaveAttributeAsync("data-filepond-item-state", new Regex("processing-(complete|error)"), new() { Timeout = 15000 });
        await Page.WaitForTimeoutAsync(800);
        await Shot("34-media-upload");
        await Page.Keyboard.PressAsync("Escape");

        await Page.SetViewportSizeAsync(390, 844);
        await Page.GotoAsync($"/admin/media?folder={folder}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Shot("35-media-mobile", fullPage: true);
    }

    [Test]
    public async Task PickerProfileAndBranding()
    {
        await LoginAsync(TestData.Admin);
        try
        {
            var folder = await CreateFolderAsync(Unique("Logos"));
            await UploadAsync("wordmark.png", MediaFiles.Png(400, 128), folderId: folder);
            await UploadAsync("icon.png", MediaFiles.Png(128, 128), folderId: folder);
            await UploadAsync("hero.jpg", MediaFiles.Jpeg(1200, 600), folderId: folder);

            await Page.GotoAsync("/admin/settings/branding");
            await Page.FillAsync("#app-name", "Acme Portal");
            await Page.ClickAsync("#branding-logo");
            var picker = Page.GetByRole(AriaRole.Dialog, new() { Name = "Choose a logo" });
            await picker.GetByRole(AriaRole.Button, new() { Name = $"Open folder" }).First.ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            var folderButton = picker.GetByRole(AriaRole.Navigation, new() { Name = "Folders" }).GetByRole(AriaRole.Button).Filter(new() { HasTextRegex = new Regex("^Logos") });
            await folderButton.First.ClickAsync();
            await picker.GetByRole(AriaRole.Button, new() { Name = "Open wordmark", Exact = true }).ClickAsync();
            await Page.WaitForTimeoutAsync(300);
            await Shot("40-media-picker");
            await picker.GetByRole(AriaRole.Button, new() { Name = "Select", Exact = true }).ClickAsync();
            await Page.WaitForTimeoutAsync(300);
            await Shot("41-branding", fullPage: true);

            await Page.GotoAsync("/account");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Upload photo" }).ClickAsync();
            await Page.Locator(".filepond--browser").SetInputFilesAsync(new FilePayload { Name = "me.jpg", MimeType = "image/jpeg", Buffer = MediaFiles.Jpeg(600, 400) });
            await ExpectToastAsync("Profile photo updated");
            await Page.WaitForTimeoutAsync(500);
            await Shot("42-profile-photo");
        }
        finally
        {
            await ApiAsync("PUT", "/api/branding", new { appName = "Panoptes", logoMediaId = (string?)null, faviconMediaId = (string?)null });
            await ApiAsync("DELETE", "/api/account/avatar");
        }
    }

    [Test]
    public async Task VersionsAndTrash()
    {
        await LoginAsync(TestData.MediaManager);
        var v1 = await UploadAsync("team-photo.jpg", MediaFiles.Jpeg(1200, 800));
        await UploadAsync("team-photo-2.jpg", MediaFiles.Jpeg(1000, 800), replaceItemId: v1.Id);
        await UploadAsync("team-photo-3.jpg", MediaFiles.Jpeg(900, 900), replaceItemId: v1.Id);

        await Page.GotoAsync("/admin/media?q=team-photo");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Open team-photo", Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Tab, new() { Name = "Versions" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Listitem)).ToHaveCountAsync(3);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(400);
        await Shot("36-media-versions");
        await Page.Keyboard.PressAsync("Escape");

        var old = await UploadAsync("old-banner.png", MediaFiles.Png(800, 300));
        await ApiAsync("POST", "/api/media/trash", new { ids = new[] { old.Id, v1.Id } });
        await Page.GotoAsync("/admin/media?view=trash");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Shot("37-media-trash");
    }

    private async Task Shot(string name, bool fullPage = false)
    {
        Directory.CreateDirectory(OutputDir);
        await Page.ScreenshotAsync(new() { Path = Path.Combine(OutputDir, $"{name}.png"), FullPage = fullPage });
    }
}
