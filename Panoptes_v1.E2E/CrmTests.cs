using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Panoptes_v1.E2E;

// Slice 2 (CRM): πελάτες, επαφές, διπλότυπα, Client 360° (docs/ux/client-360.md)
public class CrmTests : E2ETest
{
    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid().ToString("N")[..6]}";

    private static string NewTaxId() => Random.Shared.NextInt64(100_000_000, 999_999_999).ToString();

    private ILocator Sheet => Page.GetByRole(AriaRole.Dialog);

    // Ένας πελάτης μέσω του API (όπως θα τον έφτιαχνε το UI), με τα cookies του τρέχοντος χρήστη
    private async Task<JsonElement> CreateClientAsync(string name, string? taxId = null, string? duplicateReason = null)
    {
        var response = await ApiAsync("POST", "/api/crm/clients", new
        {
            kind = "Organization",
            displayName = name,
            taxId,
            duplicateReason,
            contactMethods = Array.Empty<object>(),
            addresses = Array.Empty<object>(),
            tags = Array.Empty<string>(),
        });
        Assert.That(response.Status, Is.EqualTo(201), await response.TextAsync());
        return (await response.JsonAsync())!.Value;
    }

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        var response = await ApiAsync("GET", url);
        Assert.That(response.Status, Is.EqualTo(200), $"GET {url}");
        return (await response.JsonAsync())!.Value;
    }

    [Test]
    public async Task NewClient_OpensItsPage_AndASecondOneWithTheSameTaxId_NeedsAReason()
    {
        await LoginAsync(TestData.ClientsManager);
        var taxId = NewTaxId();
        var first = Unique("Beta Ltd");

        await Page.GotoAsync("/clients");
        await Page.GetByRole(AriaRole.Button, new() { Name = "New client" }).ClickAsync();
        await Sheet.GetByLabel("Name", new() { Exact = true }).FillAsync(first);
        await Sheet.GetByLabel("Legal name").FillAsync($"{first} Μονοπρόσωπη ΙΚΕ");
        await Sheet.GetByLabel("Tax ID (ΑΦΜ)").FillAsync(taxId);
        await Sheet.GetByLabel("Email, number or address").First.FillAsync("info@beta.example");
        await Sheet.GetByRole(AriaRole.Button, new() { Name = "Create client" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"/clients/[0-9a-f-]{36}$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = first, Level = 1 })).ToBeVisibleAsync();
        await Expect(Page.GetByText(new Regex(@"^CL-\d{4,}$"))).ToBeVisibleAsync();
        await Expect(Page.GetByText($"ΑΦΜ {taxId}")).ToBeVisibleAsync();
        // Νέος πελάτης: πρώτα βήματα στην Επισκόπηση, και η δημιουργία στο ιστορικό
        await Expect(Page.GetByText("Get started")).ToBeVisibleAsync();
        await Expect(Page.GetByText(new Regex($"{Regex.Escape(TestData.ClientsManager)} created the client as CL-"))).ToBeVisibleAsync();

        // Δεύτερος με τον ίδιο ΑΦΜ (γραμμένο αλλιώς): προειδοποίηση όσο γράφεις, αιτιολογία στο Save
        await Page.GotoAsync("/clients");
        await Page.GetByRole(AriaRole.Button, new() { Name = "New client" }).ClickAsync();
        var second = Unique("Beta Branch");
        await Sheet.GetByLabel("Name", new() { Exact = true }).FillAsync(second);
        await Sheet.GetByLabel("Tax ID (ΑΦΜ)").FillAsync($"EL {taxId}");
        var warnings = Sheet.GetByTestId("client-duplicates");
        await Expect(warnings).ToContainTextAsync("Another client has this tax ID");
        await Expect(warnings.GetByRole(AriaRole.Link, new() { Name = first })).ToBeVisibleAsync();

        await Sheet.GetByRole(AriaRole.Button, new() { Name = "Create client" }).ClickAsync();
        await Expect(Sheet.GetByLabel("Why is this a separate client?")).ToBeVisibleAsync();
        await Sheet.GetByRole(AriaRole.Button, new() { Name = "Save anyway" }).ClickAsync();
        await Expect(Sheet.GetByText("Write a short reason to save anyway.")).ToBeVisibleAsync();

        await Sheet.GetByLabel("Why is this a separate client?").FillAsync("Branch billed separately");
        await Sheet.GetByRole(AriaRole.Button, new() { Name = "Save anyway" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = second, Level = 1 })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Activity", Exact = true }).ClickAsync();
        await Expect(Page.GetByText("“Branch billed separately”", new() { Exact = false })).ToBeVisibleAsync();

        // Το API λέει το ίδιο, με σταθερό code για το UI
        var refused = await ApiAsync("POST", "/api/crm/clients", new { kind = "Organization", displayName = Unique("Gamma"), taxId });
        Assert.That(refused.Status, Is.EqualTo(409));
        var problem = (await refused.JsonAsync())!.Value;
        Assert.That(problem.GetProperty("code").GetString(), Is.EqualTo("crm.duplicateTaxId"));
        Assert.That(problem.GetProperty("details").GetProperty("matches").GetArrayLength(), Is.EqualTo(2));
    }

    [Test]
    public async Task SimilarName_IsASoftWarning_ThatDoesNotBlock()
    {
        await LoginAsync(TestData.ClientsManager);
        var name = Unique("Northwind Traders");
        await CreateClientAsync(name);

        await Page.GotoAsync("/clients");
        await Page.GetByRole(AriaRole.Button, new() { Name = "New client" }).ClickAsync();
        await Sheet.GetByLabel("Name", new() { Exact = true }).FillAsync($"{name} SA");
        var warnings = Sheet.GetByTestId("client-duplicates");
        await Expect(warnings).ToContainTextAsync("Similar clients already exist");
        await Expect(warnings).ToContainTextAsync("similar name");

        await Sheet.GetByRole(AriaRole.Button, new() { Name = "Create client" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"{name} SA", Level = 1 })).ToBeVisibleAsync();
    }

    // CRM-01/02: ένα πρόσωπο σε δύο πελάτες. Κάθε πελάτης δείχνει τον άλλον μόνο μέσω της επαφής ("Also contact for").
    [Test]
    public async Task SharedContact_LinksTheTwoClientsOnlyThroughThePerson_AndRemovingKeepsThePerson()
    {
        await LoginAsync(TestData.ClientsManager);
        var beta = await CreateClientAsync(Unique("Beta"));
        var gamma = await CreateClientAsync(Unique("Gamma"));
        var maria = Unique("Maria");
        var betaId = beta.GetProperty("id").GetString();
        var gammaId = gamma.GetProperty("id").GetString();
        var dialog = Page.GetByRole(AriaRole.Dialog);

        // Νέο πρόσωπο στον πρώτο πελάτη (κύρια επαφή, αφού δεν έχει άλλη)
        await Page.GotoAsync($"/clients/{betaId}");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add contact" }).ClickAsync();
        await dialog.GetByLabel("First name").FillAsync(maria);
        await dialog.GetByLabel("Last name").FillAsync("Papadopoulou");
        await dialog.GetByLabel("Role").FillAsync("CEO");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Add contact" }).ClickAsync();
        var contact = Page.GetByTestId("client-contact").Filter(new() { HasText = maria });
        await Expect(contact).ToContainTextAsync("Primary");
        await Expect(contact).ToContainTextAsync("CEO");
        // Η κύρια επαφή και στην κεφαλίδα
        await Expect(Page.GetByTestId("client-header")).ToContainTextAsync($"{maria} Papadopoulou");
        await Expect(Page.GetByText("Get started")).ToHaveCountAsync(0);

        // Το ίδιο πρόσωπο στον δεύτερο: "Existing person"
        await Page.GotoAsync($"/clients/{gammaId}");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add contact" }).ClickAsync();
        await dialog.GetByRole(AriaRole.Radio, new() { Name = "Existing person" }).ClickAsync();
        await dialog.GetByLabel("Find a person").FillAsync(maria);
        await dialog.GetByRole(AriaRole.Option).Filter(new() { HasText = maria }).ClickAsync();
        await dialog.GetByLabel("Role").FillAsync("Owner");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Add contact" }).ClickAsync();

        var shared = Page.GetByTestId("client-contact").Filter(new() { HasText = maria });
        await Expect(shared).ToContainTextAsync("Owner");
        await Expect(shared).ToContainTextAsync("Also contact for");
        await shared.GetByRole(AriaRole.Link, new() { Name = beta.GetProperty("displayName").GetString() }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($"/clients/{betaId}$"));

        // Αφαίρεση από τον δεύτερο: μόνο η σχέση
        await Page.GotoAsync($"/clients/{gammaId}");
        await shared.GetByRole(AriaRole.Button, new() { Name = $"Actions for {maria} Papadopoulou" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Remove from client" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alertdialog)).ToContainTextAsync("The person stays in Contacts");
        await Page.GetByRole(AriaRole.Alertdialog).GetByRole(AriaRole.Button, new() { Name = "Remove from client" }).ClickAsync();
        await ExpectToastAsync("removed from this client");
        await Expect(Page.GetByText("No contacts yet.")).ToBeVisibleAsync();

        // Στις Επαφές υπάρχει ακόμα, με τον πρώτο πελάτη μόνο
        await Page.GotoAsync($"/contacts?search={Uri.EscapeDataString(maria)}");
        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = maria });
        await Expect(row).ToContainTextAsync(beta.GetProperty("displayName").GetString()!);
        await Expect(row).Not.ToContainTextAsync(gamma.GetProperty("displayName").GetString()!);
    }

    // CRM-06: η αρχειοθέτηση δεν σβήνει και δεν αλλάζει τίποτα άλλο· ο πελάτης φεύγει μόνο από την προεπιλεγμένη λίστα
    [Test]
    public async Task Archive_ShowsABanner_HidesTheClientFromTheDefaultList_AndUnarchiveBringsItBack()
    {
        await LoginAsync(TestData.ClientsManager);
        var name = Unique("Delta Archive");
        var client = await CreateClientAsync(name);
        var id = client.GetProperty("id").GetString();

        await Page.GotoAsync($"/clients/{id}");
        await Page.GetByRole(AriaRole.Button, new() { Name = "More actions" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Archive" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alertdialog)).ToContainTextAsync("Nothing else changes");
        await Page.GetByRole(AriaRole.Alertdialog).GetByRole(AriaRole.Button, new() { Name = "Archive" }).ClickAsync();

        var banner = Page.GetByTestId("archived-banner");
        await Expect(banner).ToContainTextAsync("Services and balances are unaffected.");
        await Expect(Page.GetByTestId("client-header")).ToContainTextAsync("Archived");
        var archived = await GetJsonAsync($"/api/crm/clients/{id}");
        Assert.That(archived.GetProperty("status").GetString(), Is.EqualTo("Active"), "the status itself doesn't change");

        await Page.GotoAsync($"/clients?search={Uri.EscapeDataString(name)}");
        await Expect(Page.GetByText("No matching clients")).ToBeVisibleAsync();
        await Page.GotoAsync($"/clients?search={Uri.EscapeDataString(name)}&status=Archived");
        await Expect(Page.GetByRole(AriaRole.Row).Filter(new() { HasText = name })).ToBeVisibleAsync();

        await Page.GotoAsync($"/clients/{id}");
        await banner.GetByRole(AriaRole.Button, new() { Name = "Unarchive" }).ClickAsync();
        await ExpectToastAsync("is active again");
        await Expect(banner).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Activity", Exact = true }).ClickAsync();
        await Expect(Page.GetByText($"{TestData.ClientsManager} archived the client")).ToBeVisibleAsync();
        await Expect(Page.GetByText($"{TestData.ClientsManager} unarchived the client")).ToBeVisibleAsync();
    }

    [Test]
    public async Task EditDetails_SavesAndRecordsTheChangedFields_AndAStaleVersionIsAConflict()
    {
        await LoginAsync(TestData.ClientsManager);
        var client = await CreateClientAsync(Unique("Epsilon"));
        var id = client.GetProperty("id").GetString();

        await Page.GotoAsync($"/clients/{id}");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Edit details" }).ClickAsync();
        await Sheet.GetByLabel("Tax office (ΔΟΥ)").FillAsync("Αθηνών");
        await Sheet.GetByLabel("Tags", new() { Exact = true }).FillAsync("web");
        await Sheet.GetByLabel("Tags", new() { Exact = true }).PressAsync("Enter");
        await Sheet.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();

        await Expect(Page.GetByText("ΔΟΥ Αθηνών")).ToBeVisibleAsync();
        await Expect(Page.GetByText(new Regex("changed the (tax office, tags|tags, tax office)"))).ToBeVisibleAsync();

        // Το version που διάβασε ο χρήστης είναι πια παλιό: 409, όχι σιωπηλή αντικατάσταση
        var stale = await ApiAsync("PUT", $"/api/crm/clients/{id}", new
        {
            kind = "Organization",
            displayName = "Overwritten",
            version = client.GetProperty("version").GetUInt32(),
        });
        Assert.That(stale.Status, Is.EqualTo(409));
    }

    // Clients.View μόνο: βλέπει, δεν αλλάζει. Χωρίς Clients.View: τίποτα (σελίδα, API, αναζήτηση).
    [Test]
    public async Task Permissions_ViewerOnlyReads_AndUsersWithoutClientsViewSeeNothing()
    {
        await LoginAsync(TestData.ClientsManager);
        var name = Unique("Zeta Private");
        var client = await CreateClientAsync(name);
        var id = client.GetProperty("id").GetString();

        await Page.Context.ClearCookiesAsync();
        await LoginAsync(TestData.ClientsViewer);
        await Page.GotoAsync($"/clients/{id}");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = name, Level = 1 })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Edit details" })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Add contact" })).ToHaveCountAsync(0);
        await Page.GotoAsync("/clients");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "New client" })).ToHaveCountAsync(0);
        Assert.That((await ApiAsync("PUT", $"/api/crm/clients/{id}", new { kind = "Organization", displayName = "x" })).Status, Is.EqualTo(403));
        Assert.That((await ApiAsync("POST", $"/api/crm/clients/{id}/archive", new { })).Status, Is.EqualTo(403));
        // Άγνωστος πελάτης: 404, όπως και χωρίς πρόσβαση
        Assert.That((await ApiAsync("GET", $"/api/crm/clients/{Guid.NewGuid()}")).Status, Is.EqualTo(404));

        await Page.Context.ClearCookiesAsync();
        await LoginAsync(TestData.MediaViewer);
        Assert.That((await ApiAsync("GET", "/api/crm/clients")).Status, Is.EqualTo(403));
        Assert.That((await ApiAsync("GET", $"/api/crm/clients/{id}")).Status, Is.EqualTo(403));
        var search = await GetJsonAsync($"/api/search?q={Uri.EscapeDataString(name)}");
        Assert.That(search.GetProperty("groups").GetArrayLength(), Is.Zero);
        await Page.GotoAsync($"/clients/{id}");
        await Expect(Page.GetByText(name)).ToHaveCountAsync(0);
        await Expect(Page.Locator("nav[aria-label='Main navigation'] a[data-title='Clients']")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task UnknownClient_ShowsOneNotFoundMessage()
    {
        await LoginAsync(TestData.ClientsManager);
        await Page.GotoAsync($"/clients/{Guid.NewGuid()}");

        await Expect(Page.GetByText("This client doesn't exist or you don't have access")).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Clients" }).Last.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/clients$"));
    }

    // Στο κινητό: χωρίς οριζόντιο scroll, τα στοιχεία σε accordion και οι ενέργειες σε σταθερή μπάρα κάτω
    [Test]
    public async Task ClientPage_OnAPhone_FitsTheScreen_WithActionsAtTheBottom()
    {
        await LoginAsync(TestData.ClientsManager);
        var client = await CreateClientAsync(Unique("Eta Mobile With A Rather Long Company Name For Small Screens"));
        await Page.SetViewportSizeAsync(390, 844);

        await Page.GotoAsync($"/clients/{client.GetProperty("id").GetString()}");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToBeVisibleAsync();
        Assert.That(await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"), Is.True);

        var about = Page.GetByRole(AriaRole.Button, new() { Name = "About & contacts" });
        await Expect(about).ToHaveAttributeAsync("aria-expanded", "false");
        await about.ClickAsync();
        await Expect(Page.GetByText("No contacts yet.")).ToBeVisibleAsync();

        var editAtTheBottom = Page.GetByRole(AriaRole.Button, new() { Name = "Edit details" });
        await Expect(editAtTheBottom).ToBeVisibleAsync();
        var box = await editAtTheBottom.BoundingBoxAsync();
        Assert.That(box!.Y, Is.GreaterThan(700));
    }

    [Test]
    public async Task Search_FindsClientsAndPeople_ByNameReferenceAndEmail()
    {
        await LoginAsync(TestData.ClientsManager);
        var name = Unique("Theta Search");
        var client = await CreateClientAsync(name);
        var reference = client.GetProperty("ref").GetString()!;

        foreach (var query in new[] { name, reference })
        {
            var result = await GetJsonAsync($"/api/search?q={Uri.EscapeDataString(query)}");
            var clients = result.GetProperty("groups").EnumerateArray().Single(g => g.GetProperty("type").GetString() == "client");
            Assert.That(clients.GetProperty("hits").EnumerateArray().Select(h => h.GetProperty("title").GetString()), Does.Contain(name), query);
        }
    }
}
