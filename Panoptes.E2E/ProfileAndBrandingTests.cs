using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Panoptes.E2E;

// Φωτογραφία προφίλ: ανεβαίνει από το My account (FilePond, τετράγωνο κόψιμο) και φαίνεται παντού
public class ProfilePhotoTests : MediaTest
{
    [Test]
    public async Task ProfilePhoto_ShowsEverywhere_AndCanBeRemoved()
    {
        await LoginAsync(TestData.AvatarUser);
        await Page.GotoAsync("/account");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Upload photo" }).ClickAsync();
        await Page.Locator(".filepond--browser").SetInputFilesAsync(new FilePayload { Name = "me.png", MimeType = "image/png", Buffer = MediaFiles.Png(600, 400) });
        await ExpectToastAsync("Profile photo updated");

        // Στο μενού χρήστη: η φωτογραφία, τετράγωνη (κόψιμο στον browser και στον server)
        var menuAvatar = Page.GetByTestId("user-menu-avatar");
        await Expect(menuAvatar).ToBeVisibleAsync();
        var src = (await menuAvatar.GetAttributeAsync("src"))!;
        var photo = await Page.APIRequest.GetAsync(src);
        Assert.That(photo.Status, Is.EqualTo(200));
        var (width, height, _) = MediaFiles.Measure(await photo.BodyAsync());
        Assert.That(width, Is.EqualTo(height), "profile photos are square");

        // Δεν μπαίνει στη βιβλιοθήκη και δεν φαίνεται χωρίς login
        Assert.That((await (await AnonymousAsync()).GetAsync(src)).Status, Is.EqualTo(404));

        // Ο admin τη βλέπει στη λίστα χρηστών
        var admin = await SecondUserAsync(TestData.Admin);
        await admin.GotoAsync("/admin/users?search=avatar%40");
        await Expect(admin.GetByRole(AriaRole.Row, new() { Name = TestData.AvatarUser, Exact = true }).Locator("img")).ToHaveAttributeAsync("src", src);
        Assert.That((await admin.APIRequest.GetAsync(src)).Status, Is.EqualTo(200));

        await Page.GetByRole(AriaRole.Button, new() { Name = "Remove" }).ClickAsync();
        await ExpectToastAsync("Profile photo removed");
        await Expect(menuAvatar).ToHaveCountAsync(0);
        Assert.That((await admin.APIRequest.GetAsync(src)).Status, Is.EqualTo(404), "the old photo is deleted");
    }

    [Test]
    public async Task ProfilePhotos_FollowTheRules()
    {
        await LoginAsync(TestData.AvatarUser2);

        // Κάθε χρήστης (χωρίς δικαιώματα media) μπορεί, αλλά μόνο εικόνες
        var pdf = await UploadAsync("me.pdf", MediaFiles.Pdf(), purpose: "Avatar");
        Assert.That(pdf.Response.Status, Is.EqualTo(400));
        Assert.That(pdf.Detail, Does.Contain("Profile photos must be"));

        var mine = await UploadAsync("me.png", MediaFiles.Png(), purpose: "Avatar");
        Assert.That(mine.Ok, Is.True, mine.Detail);
        Assert.That((await ApiAsync("PUT", "/api/account/avatar", new { mediaId = mine.Id })).Status, Is.EqualTo(200));

        // Η φωτογραφία κάποιου άλλου, ή ένα αρχείο της βιβλιοθήκης, δεν γίνεται δική σου
        var other = await SecondUserAsync(TestData.MediaEditor);
        var theirs = await UploadAsync(other, "them.png", MediaFiles.Png(), purpose: "Avatar");
        var library = await UploadAsync(other, "library.png", MediaFiles.Png());
        Assert.That((await ApiAsync("PUT", "/api/account/avatar", new { mediaId = theirs.Id })).Status, Is.EqualTo(400));
        Assert.That((await ApiAsync("PUT", "/api/account/avatar", new { mediaId = library.Id })).Status, Is.EqualTo(400));

        // Οι φωτογραφίες προφίλ δεν εμφανίζονται στη βιβλιοθήκη
        Assert.That((await ApiAsync(other, "GET", $"/api/media/{mine.Id}")).Status, Is.EqualTo(404));
        var session = (await (await ApiAsync("GET", "/api/auth/session")).JsonAsync())!.Value;
        Assert.That(session.GetProperty("user").GetProperty("avatarUrl").GetString(), Does.StartWith("/media/"));
    }
}

// Branding: όνομα, λογότυπο (από τη βιβλιοθήκη μέσω του picker) και favicon (upload μέσα στον picker)
public class BrandingTests : MediaTest
{
    [TearDown]
    public async Task ResetBrandingAsync()
    {
        var admin = await SecondUserAsync(TestData.Admin);
        await ApiAsync(admin, "PUT", "/api/branding", new { appName = "Panoptes", logoMediaId = (string?)null, faviconMediaId = (string?)null });
    }

    [Test]
    public async Task Branding_PickedImagesShowOnPublicPages()
    {
        await LoginAsync(TestData.Admin);
        var folder = await CreateFolderAsync(Unique("Brand assets"));
        var logoTitle = Unique("acme-logo");
        var logo = await UploadAsync($"{logoTitle}.png", MediaFiles.Png(400, 128), folderId: folder);

        await Page.GotoAsync("/admin/settings/branding");
        await Page.FillAsync("#app-name", "Acme Portal");

        // Λογότυπο: από τη βιβλιοθήκη (αναζήτηση μέσα στον picker)
        await Page.ClickAsync("#branding-logo");
        var picker = Page.GetByRole(AriaRole.Dialog, new() { Name = "Choose a logo" });
        await picker.GetByRole(AriaRole.Searchbox).FillAsync(logoTitle);
        await picker.GetByRole(AriaRole.Button, new() { Name = $"Open {logoTitle}", Exact = true }).ClickAsync();
        await Expect(picker).ToContainTextAsync($"Selected: {logoTitle}");
        await picker.GetByRole(AriaRole.Button, new() { Name = "Select", Exact = true }).ClickAsync();
        await Expect(Page.GetByText(logoTitle)).ToBeVisibleAsync();

        // Favicon: ανέβασμα μέσα από τον picker (επιλέγεται αυτόματα)
        await Page.ClickAsync("#branding-favicon");
        var faviconPicker = Page.GetByRole(AriaRole.Dialog, new() { Name = "Choose a favicon" });
        await faviconPicker.GetByRole(AriaRole.Tab, new() { Name = "Upload" }).ClickAsync();
        await faviconPicker.Locator(".filepond--browser").SetInputFilesAsync(new FilePayload { Name = "acme-icon.png", MimeType = "image/png", Buffer = MediaFiles.Png(128, 128) });
        await Expect(faviconPicker).ToContainTextAsync("Selected: acme-icon");
        await faviconPicker.GetByRole(AriaRole.Button, new() { Name = "Select", Exact = true }).ClickAsync();

        await Expect(Page.GetByText("The selected images will be made public")).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await ExpectToastAsync("Branding updated");

        // Οι εικόνες έγιναν δημόσιες
        var logoItem = await GetJsonAsync($"/api/media/{logo.Id}");
        Assert.That(logoItem.GetProperty("visibility").GetString(), Is.EqualTo("Public"));

        // Ο επισκέπτης βλέπει το νέο όνομα, λογότυπο και τίτλο
        var visitor = await (await Browser.NewContextAsync(ContextOptions())).NewPageAsync();
        await visitor.GotoAsync("/login");
        await Expect(visitor.GetByTestId("app-name")).ToHaveTextAsync("Acme Portal");
        await Expect(visitor).ToHaveTitleAsync("Acme Portal");
        await Expect(visitor.Locator("main a[href='/'] img")).ToHaveAttributeAsync("src", new Regex("^/media/p/"));
        await Expect(visitor.Locator("link[rel=icon]")).ToHaveAttributeAsync("href", new Regex("^/media/p/"));

        // Το λογότυπο δεν πάει στον κάδο ούτε γίνεται ιδιωτικό όσο χρησιμοποιείται
        var trash = await ApiAsync("POST", "/api/media/trash", new { ids = new[] { logo.Id } });
        Assert.That(trash.Status, Is.EqualTo(409));
        Assert.That(await trash.TextAsync(), Does.Contain("is used as the app logo"));
        var makePrivate = await ApiAsync("PUT", $"/api/media/{logo.Id}", new { title = logoTitle, visibility = "Private" });
        Assert.That(makePrivate.Status, Is.EqualTo(409));
    }

    [Test]
    public async Task Branding_IsPublicToRead_ButEditingNeedsPermission()
    {
        var anonymous = await AnonymousAsync();
        var branding = await anonymous.GetAsync("/api/branding");
        Assert.That(branding.Status, Is.EqualTo(200));
        Assert.That((await branding.JsonAsync())!.Value.GetProperty("appName").GetString(), Is.EqualTo("Panoptes"));

        await LoginAsync(TestData.SettingsViewer);
        Assert.That((await ApiAsync("GET", "/api/branding/settings")).Status, Is.EqualTo(200));
        Assert.That((await ApiAsync("PUT", "/api/branding", new { appName = "Hacked" })).Status, Is.EqualTo(403));

        await Page.GotoAsync("/admin/settings/branding");
        await Expect(Page.GetByText("only people with the Settings · Edit permission can change them")).ToBeVisibleAsync();
        await Expect(Page.Locator("#app-name")).ToBeDisabledAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" })).ToHaveCountAsync(0);

        var noAccess = await SecondUserAsync(TestData.NoAccess);
        Assert.That((await ApiAsync(noAccess, "GET", "/api/branding/settings")).Status, Is.EqualTo(403));
    }
}
