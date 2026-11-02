using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Panoptes_v1.E2E;

// Client 360° → Files (DOC-01): ανέβασμα και σύνδεση, σύνδεση από τη βιβλιοθήκη, αποσύνδεση που κρατά το αρχείο,
// και αναζήτηση εγγραφών από το Ctrl+K
public class CrmFilesTests : MediaTest
{
    private ILocator FilePondInput => Page.Locator(".filepond--browser");

    private async Task<string> CreateClientAsync(string name)
    {
        var response = await ApiAsync("POST", "/api/crm/clients", new { kind = "Organization", displayName = name });
        Assert.That(response.Status, Is.EqualTo(201), await response.TextAsync());
        return (await response.JsonAsync())!.Value.GetProperty("id").GetString()!;
    }

    private ILocator FileRow(string title) => Page.GetByRole(AriaRole.Row).Filter(new() { HasText = title });

    [Test]
    public async Task Upload_FromTheFilesTab_LinksTheFile_AndUndoUnlinksAndRemovesIt()
    {
        await LoginAsync(TestData.Admin);
        var clientId = await CreateClientAsync(Unique("Files Client"));
        var contract = Unique("contract");
        var draft = Unique("draft");

        await Page.GotoAsync($"/clients/{clientId}/files");
        await Expect(Page.GetByText("No files yet")).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        var dialog = Page.GetByRole(AriaRole.Dialog, new() { Name = "Upload files" });
        await FilePondInput.SetInputFilesAsync(new FilePayload { Name = $"{contract}.pdf", MimeType = "application/pdf", Buffer = MediaFiles.Pdf() });
        await Expect(Page.Locator(".filepond--item")).ToHaveAttributeAsync("data-filepond-item-state", "processing-complete");
        await Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Done · 1 file attached" })).ToBeVisibleAsync();

        // Undo ενός δεύτερου: πρώτα αποσύνδεση, μετά αναίρεση (αλλιώς ο server αρνείται να σβήσει συνδεδεμένο αρχείο)
        await FilePondInput.SetInputFilesAsync(new FilePayload { Name = $"{draft}.pdf", MimeType = "application/pdf", Buffer = MediaFiles.Pdf() });
        var draftItem = Page.Locator(".filepond--item").Filter(new() { HasText = draft });
        await Expect(draftItem).ToHaveAttributeAsync("data-filepond-item-state", "processing-complete");
        await draftItem.Locator(".filepond--action-revert-item-processing").ClickAsync();
        await Expect(draftItem).ToHaveCountAsync(0);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Done · 1 file attached" }).ClickAsync();

        await Expect(FileRow(contract)).ToBeVisibleAsync();
        await Expect(FileRow(contract)).ToContainTextAsync("This client");
        await Expect(FileRow(draft)).ToHaveCountAsync(0);
        var library = await GetJsonAsync($"/api/media?search={Uri.EscapeDataString(draft)}");
        Assert.That(library.GetProperty("items").GetArrayLength(), Is.Zero, "the undone upload is gone from the library");

        await Page.GetByRole(AriaRole.Link, new() { Name = "Activity", Exact = true }).ClickAsync();
        await Expect(Page.GetByText($"{TestData.Admin} attached {contract}")).ToBeVisibleAsync();
    }

    [Test]
    public async Task AttachFromTheLibrary_ThenUnlink_KeepsTheFileInTheLibrary()
    {
        await LoginAsync(TestData.Admin);
        var clientId = await CreateClientAsync(Unique("Library Client"));
        var title = Unique("brief");
        var upload = await UploadAsync($"{title}.pdf", MediaFiles.Pdf());
        Assert.That(upload.Ok, Is.True, upload.Detail);

        await Page.GotoAsync($"/clients/{clientId}/files");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Attach from library" }).ClickAsync();
        var picker = Page.GetByRole(AriaRole.Dialog, new() { Name = "Attach files from the library" });
        await picker.GetByRole(AriaRole.Searchbox).FillAsync(title);
        await picker.GetByRole(AriaRole.Button, new() { Name = $"Open {title}", Exact = true }).ClickAsync();
        await picker.GetByRole(AriaRole.Button, new() { Name = "Select", Exact = true }).ClickAsync();
        await ExpectToastAsync("1 file attached");
        await Expect(FileRow(title)).ToBeVisibleAsync();

        // Συνδεδεμένο αρχείο: δεν πάει στον κάδο (το λέει ο server της βιβλιοθήκης)
        var trash = await ApiAsync("POST", "/api/media/trash", new { ids = new[] { upload.Id } });
        Assert.That(trash.Status, Is.EqualTo(409));

        await FileRow(title).GetByRole(AriaRole.Button, new() { Name = $"Unlink {title}" }).ClickAsync();
        var confirm = Page.GetByRole(AriaRole.Alertdialog);
        await Expect(confirm).ToContainTextAsync("This removes the link only");
        await confirm.GetByRole(AriaRole.Button, new() { Name = "Unlink" }).ClickAsync();
        await ExpectToastAsync($"{title} unlinked");
        await Expect(Page.GetByText("No files yet")).ToBeVisibleAsync();

        // Στη βιβλιοθήκη υπάρχει ακόμα
        Assert.That((await ApiAsync("GET", $"/api/media/{upload.Id}")).Status, Is.EqualTo(200));
    }

    // Χωρίς Media.View: δεν υπάρχει καρτέλα Files (τα αρχεία δεν κατεβαίνουν ούτως ή άλλως) και το API δεν δίνει τίποτα
    [Test]
    public async Task WithoutMediaView_TheFilesTabIsHidden()
    {
        await LoginAsync(TestData.ClientsViewer);
        var response = await ApiAsync("GET", "/api/crm/clients?pageSize=1");
        var first = (await response.JsonAsync())!.Value.GetProperty("items").EnumerateArray().FirstOrDefault();
        if (first.ValueKind == System.Text.Json.JsonValueKind.Undefined)
        {
            await Page.GotoAsync("about:blank");
        await Page.Context.ClearCookiesAsync();
            await LoginAsync(TestData.ClientsManager);
            var id = await CreateClientAsync(Unique("No Files"));
            await Page.GotoAsync("about:blank");
        await Page.Context.ClearCookiesAsync();
            await LoginAsync(TestData.ClientsViewer);
            await Page.GotoAsync($"/clients/{id}");
        }
        else
        {
            await Page.GotoAsync($"/clients/{first.GetProperty("id").GetString()}");
        }

        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Activity", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Files", Exact = true })).ToHaveCountAsync(0);
        var files = await GetJsonAsync(new Uri(Page.Url).AbsolutePath.Replace("/clients/", "/api/crm/clients/") + "/files");
        Assert.That(files.GetArrayLength(), Is.Zero);
    }

    // Ctrl+K: πελάτες και επαφές από το /api/search, και μετάβαση στη σελίδα τους
    [Test]
    public async Task CommandPalette_FindsAClientByReference_AndOpensIt()
    {
        await LoginAsync(TestData.Admin);
        var name = Unique("Palette Client");
        var id = await CreateClientAsync(name);
        var client = await GetJsonAsync($"/api/crm/clients/{id}");
        var reference = client.GetProperty("ref").GetString()!;

        await Page.GotoAsync("/admin");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Search or jump to…" })).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Control+K");
        var palette = Page.GetByRole(AriaRole.Dialog, new() { Name = "Command palette" });
        await palette.GetByRole(AriaRole.Combobox).FillAsync(reference);
        var hit = palette.GetByRole(AriaRole.Option).Filter(new() { HasText = name });
        await Expect(hit).ToContainTextAsync(reference);
        await Expect(palette.GetByText("Clients", new() { Exact = true })).ToBeVisibleAsync();
        await hit.ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex($"/clients/{id}$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = name, Level = 1 })).ToBeVisibleAsync();
    }
}
