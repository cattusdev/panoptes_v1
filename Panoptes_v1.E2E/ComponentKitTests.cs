using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Panoptes_v1.E2E;

// Τα κοινά components του Slice 1 (form kit, DataTable v2, StatusBadge, MoneyText, Alert) στη δοκιμαστική σελίδα
// /admin/dev/kit, που υπάρχει μόνο με τα test endpoints. Οι πραγματικές οθόνες τα ελέγχουν και στα δικά τους tests.
public class ComponentKitTests : E2ETest
{
    private ILocator Form => Page.Locator("form", new() { Has = Page.Locator("#kit-amount") });
    private ILocator Amount => Page.Locator("#kit-amount");
    private ILocator Submit => Form.GetByRole(AriaRole.Button, new() { Name = "Submit" });

    private async Task OpenKitAsync(string email = TestData.Admin)
    {
        await LoginAsync(email);
        await Page.GotoAsync("/admin/dev/kit");
        await Expect(Amount).ToBeVisibleAsync();
    }

    [Test]
    public async Task Form_ShowsEveryErrorUnderItsField_AndFocusesTheFirst()
    {
        await OpenKitAsync();

        await Submit.ClickAsync();

        await Expect(Form.GetByText("This field is required.")).ToHaveCountAsync(3); // ποσό, ημερομηνία, πελάτης
        await Expect(Form.GetByText("Choose at least 1 option.")).ToBeVisibleAsync();
        await Expect(Form.GetByText("Use at least 3 characters.")).ToBeVisibleAsync();
        await Expect(Amount).ToBeFocusedAsync();
        await Expect(Amount).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(Page.GetByTestId("kit-result")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task MoneyInput_AcceptsBothNumberStyles_AndRejectsNonAmounts()
    {
        await OpenKitAsync();

        await Amount.FillAsync("1.234,50");
        await Amount.BlurAsync();
        await Expect(Amount).ToHaveValueAsync("1,234.50");

        await Amount.FillAsync("12,345");
        await Amount.BlurAsync();
        await Expect(Amount).ToHaveValueAsync("12,345.00");

        await Amount.FillAsync("12.3.4");
        await Amount.BlurAsync();
        await Expect(Form.GetByText("Enter an amount, e.g. 1,234.50.")).ToBeVisibleAsync();
        await Expect(Amount).ToHaveValueAsync("12.3.4"); // Δεν "διορθώνουμε" σιωπηλά ό,τι έγραψε
    }

    [Test]
    public async Task ValidForm_SubmitsTypedValues()
    {
        await OpenKitAsync();

        await Amount.FillAsync("1.234,50");

        await Page.Locator("#kit-due").ClickAsync();
        await Page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Today", Exact = true }).ClickAsync();
        await Expect(Page.Locator("#kit-due")).Not.ToHaveTextAsync("Pick a date");

        await Page.Locator("#kit-client").ClickAsync();
        await Page.GetByPlaceholder("Search…").FillAsync("cyc");
        await Expect(Page.GetByRole(AriaRole.Option)).ToHaveCountAsync(1);
        await Page.GetByRole(AriaRole.Option, new() { Name = "Cyclades Travel" }).ClickAsync();
        await Expect(Page.Locator("#kit-client")).ToHaveTextAsync("Cyclades Travel");

        await Page.Locator("#kit-tags").ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = "Hosting" }).ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = "Domain" }).ClickAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.Locator("#kit-tags")).ToContainTextAsync("Hosting");
        await Expect(Page.Locator("#kit-tags")).ToContainTextAsync("Domain");

        await Page.Locator("#kit-note").FillAsync("  Renewal for 2027  ");
        await Submit.ClickAsync();

        var today = DateTime.Today.ToString("yyyy-MM-dd");
        await Expect(Page.GetByTestId("kit-result")).ToHaveTextAsync(
            $"{{\"amount\":1234.5,\"dueOn\":\"{today}\",\"client\":\"CL-0043\",\"tags\":[\"hosting\",\"domain\"],\"note\":\"Renewal for 2027\"}}");
    }

    [Test]
    public async Task MoneyAndBadgesAndAlerts_FollowTheSharedRules()
    {
        await OpenKitAsync();

        await Expect(Page.GetByTestId("money-plain")).ToHaveTextAsync("€1,240.00");
        await Expect(Page.GetByTestId("money-credit")).ToHaveTextAsync("-€50.00Credit");
        await Expect(Page.GetByTestId("money-unknown")).ToHaveTextAsync("Unknown");

        // Κάθε σημασία έχει κείμενο· το "historical" και ρολόι
        var badges = Page.GetByTestId("kit-badges").Locator("[data-slot=badge]");
        await Expect(badges).ToHaveTextAsync(["Overdue", "Pending approval", "In progress", "Paid", "Archived", "Historical"]);
        await Expect(badges.Last.Locator("svg")).ToHaveCountAsync(1);

        await Expect(Page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "Needs attention" })).ToHaveAttributeAsync("class", new Regex("text-warning"));
        await Expect(Page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "For your information" })).ToHaveAttributeAsync("class", new Regex("text-info"));
    }

    [Test]
    public async Task InGreek_MoneyDatesAndMessagesFollowTheLanguage()
    {
        await LoginAsync(TestData.Auditor);
        var language = await ApiAsync("PUT", "/api/account/language", new { language = "el" });
        Assert.That(language.Status, Is.EqualTo(204));
        try
        {
            await CheckGreekAsync();
        }
        finally
        {
            await ApiAsync("PUT", "/api/account/language", new { language = (string?)null });
        }
    }

    private async Task CheckGreekAsync()
    {
        await Page.GotoAsync("/admin/dev/kit");
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "el");

        await Expect(Page.GetByTestId("money-plain")).ToHaveTextAsync(new Regex("^1\\.240,00\\s€$"));
        await Expect(Page.GetByTestId("money-credit")).ToContainTextAsync("Πίστωση");
        await Expect(Page.GetByTestId("money-unknown")).ToHaveTextAsync("Άγνωστο");

        // Ελληνικά: το "," είναι δεκαδικό, το "." χιλιάδες (και το 12.5 το καταλαβαίνουμε ως 12,50)
        await Amount.FillAsync("1.234,5");
        await Amount.BlurAsync();
        await Expect(Amount).ToHaveValueAsync("1.234,50");
        await Amount.FillAsync("12.5");
        await Amount.BlurAsync();
        await Expect(Amount).ToHaveValueAsync("12,50");

        await Form.GetByRole(AriaRole.Button, new() { Name = "Υποβολή" }).ClickAsync();
        await Expect(Form.GetByText("Το πεδίο είναι υποχρεωτικό.")).ToHaveCountAsync(2);
    }

    [Test]
    public async Task Table_SortsSelectsAndHidesColumns()
    {
        await OpenKitAsync();
        var table = Page.GetByRole(AriaRole.Table);
        var nameHeader = table.GetByRole(AriaRole.Columnheader, new() { Name = "Name" });

        await Expect(nameHeader).ToHaveAttributeAsync("aria-sort", "ascending");
        await Expect(table.GetByRole(AriaRole.Row).Nth(1)).ToHaveAccessibleNameAsync("Acme Ltd");
        await nameHeader.GetByRole(AriaRole.Button).ClickAsync();
        await Expect(nameHeader).ToHaveAttributeAsync("aria-sort", "descending");
        await Expect(table.GetByRole(AriaRole.Row).Nth(1)).ToHaveAccessibleNameAsync("Delta Logistics");

        // Επιλογή γραμμών -> μπάρα μαζικών ενεργειών
        var bulk = Page.GetByRole(AriaRole.Region, new() { Name = "Bulk actions" });
        await Expect(bulk).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Checkbox, new() { Name = "Select Acme Ltd" }).ClickAsync();
        await Page.GetByRole(AriaRole.Checkbox, new() { Name = "Select Cyclades Travel" }).ClickAsync();
        await Expect(bulk).ToContainTextAsync("2 selected");
        await bulk.GetByRole(AriaRole.Button, new() { Name = "Archive" }).ClickAsync();
        await ExpectToastAsync("2 rows archived");
        await Expect(bulk).ToHaveCountAsync(0);

        await Page.GetByRole(AriaRole.Checkbox, new() { Name = "Select all rows on this page" }).ClickAsync();
        await Expect(bulk).ToContainTextAsync("4 selected");
        await bulk.GetByRole(AriaRole.Button, new() { Name = "Clear" }).ClickAsync();
        await Expect(bulk).ToHaveCountAsync(0);

        // Στήλες: κρύβεται και το θυμάται μετά από reload
        await Page.GetByRole(AriaRole.Button, new() { Name = "Columns" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitemcheckbox, new() { Name = "Balance" }).ClickAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(table.GetByRole(AriaRole.Columnheader, new() { Name = "Balance" })).ToHaveCountAsync(0);
        await Page.ReloadAsync();
        await Expect(table.GetByRole(AriaRole.Columnheader, new() { Name = "Status" })).ToBeVisibleAsync();
        await Expect(table.GetByRole(AriaRole.Columnheader, new() { Name = "Balance" })).ToHaveCountAsync(0);
    }
}
