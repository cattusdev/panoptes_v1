using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Panoptes_v1.E2E;

// Η σελίδα των δουλειών παρασκηνίου (/admin/jobs, μόνο Admin): οι αποτυχίες φαίνονται, η λεπτομέρεια δείχνει
// λάθος και payload, επανάληψη / ακύρωση από τη λεπτομέρεια και μαζική επανάληψη (όλες ή καμία)
public class JobsPageTests : MediaTest
{
    private ILocator Details => Page.GetByRole(AriaRole.Dialog);

    [Test]
    public async Task FailedJob_StandsOut_AndIsRetriedOrCancelledFromItsDetails()
    {
        await LoginAsync(TestData.Admin);
        var key = Unique("failing");
        await EnqueueAsync(key, failTimes: 5, maxAttempts: 1);
        await RunJobsAsync();

        await Page.GotoAsync("/admin");
        await Page.Locator("nav[aria-label='Main navigation'] a[data-title='Jobs']").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/jobs$"));
        await Expect(Page.GetByText(new Regex(@"^\d+ jobs? failed$"))).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Show failed" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("status=Failed"));

        await Page.GetByPlaceholder("Search by type or key").FillAsync(key);
        var row = Row($"test.record job test:{key}");
        await Expect(row).ToContainTextAsync("Failed");
        await Expect(row).ToContainTextAsync("1 of 1");
        await Expect(row).ToContainTextAsync("Simulated failure 1 of 5");

        // Λεπτομέρεια: το λάθος και το payload (JSON με εσοχές)
        await row.ClickAsync();
        await Expect(Details.GetByTestId("job-error")).ToContainTextAsync("Simulated failure 1 of 5");
        await Expect(Details.GetByTestId("job-payload")).ToContainTextAsync($"\"key\": \"{key}\"");

        await Details.GetByRole(AriaRole.Button, new() { Name = "Retry" }).ClickAsync();
        await ExpectToastAsync("Job queued again");
        await Expect(Details.GetByText("Pending", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Details.GetByRole(AriaRole.Button, new() { Name = "Retry" })).ToHaveCountAsync(0);

        // Ακύρωση μιας δουλειάς σε αναμονή: με επιβεβαίωση
        await Details.GetByRole(AriaRole.Button, new() { Name = "Cancel job" }).ClickAsync();
        var confirm = Page.GetByRole(AriaRole.Alertdialog);
        await Expect(confirm.GetByText("Cancel this job?")).ToBeVisibleAsync();
        await confirm.GetByRole(AriaRole.Button, new() { Name = "Cancel job" }).ClickAsync();
        await ExpectToastAsync("Job cancelled");
        Assert.That((await JobAsync(key)).GetProperty("status").GetString(), Is.EqualTo("Cancelled"));

        await RunJobsAsync();
        Assert.That(await RunsAsync(key), Is.EqualTo(1), "a cancelled job never runs");
    }

    [Test]
    public async Task BulkRetry_QueuesTheSelectedJobs_AllOrNothing()
    {
        await LoginAsync(TestData.Admin);
        var prefix = Unique("bulk").Replace(' ', '-');
        await EnqueueAsync($"{prefix}-a", failTimes: 5, maxAttempts: 1);
        await EnqueueAsync($"{prefix}-b", failTimes: 5, maxAttempts: 1);
        await RunJobsAsync();

        await Page.GotoAsync($"/admin/jobs?search={Uri.EscapeDataString(prefix)}");
        await Expect(Page.GetByRole(AriaRole.Row)).ToHaveCountAsync(3); // header + 2
        await Page.GetByRole(AriaRole.Checkbox, new() { Name = "Select all rows on this page" }).CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Retry selected" }).ClickAsync();
        await ExpectToastAsync("2 jobs queued again");
        Assert.That((await JobAsync($"{prefix}-a")).GetProperty("status").GetString(), Is.EqualTo("Pending"));
        Assert.That((await JobAsync($"{prefix}-b")).GetProperty("status").GetString(), Is.EqualTo("Pending"));

        // Μόνο αποτυχημένες / ακυρωμένες: με μία σε αναμονή στην επιλογή, το κουμπί δεν ενεργοποιείται
        await Page.GetByRole(AriaRole.Checkbox, new() { Name = $"Select test.record job test:{prefix}-a" }).CheckAsync();
        await Expect(Page.GetByText("Only failed or cancelled jobs can be retried.")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Retry selected" })).ToBeDisabledAsync();

        // Ο server: όλες ή καμία
        await EnqueueAsync($"{prefix}-c", failTimes: 5, maxAttempts: 1);
        await RunJobsAsync(); // Και οι a, b ξανατρέχουν και αποτυγχάνουν· εδώ μετράει μόνο η c
        var c = (await JobAsync($"{prefix}-c")).GetProperty("id").GetString();
        var pending = await PendingJobIdAsync();
        var mixed = await ApiAsync("POST", "/api/jobs/retry", new { ids = new[] { c, pending } });
        Assert.That(mixed.Status, Is.EqualTo(409));
        Assert.That(await mixed.TextAsync(), Does.Contain("Some of the selected jobs can't be retried any more."));
        Assert.That((await JobAsync($"{prefix}-c")).GetProperty("status").GetString(), Is.EqualTo("Failed"), "nothing changed");
    }

    [Test]
    public async Task JobsPage_IsOnlyForTheAdmin()
    {
        // Ο MediaManager έχει πρόσβαση στο console, όχι στα jobs
        await LoginAsync(TestData.MediaManager);
        await Page.GotoAsync("/admin");
        await Expect(Page.Locator("nav[aria-label='Main navigation'] a[data-title='Media']")).ToBeVisibleAsync();
        await Expect(Page.Locator("nav[aria-label='Main navigation'] a[data-title='Jobs']")).ToHaveCountAsync(0);

        await Page.GotoAsync("/admin/jobs");
        await Expect(Page.GetByTestId("no-access")).ToBeVisibleAsync();
        Assert.That((await ApiAsync("GET", "/api/jobs/summary")).Status, Is.EqualTo(403));
        Assert.That((await ApiAsync("POST", "/api/jobs/retry", new { ids = new[] { Guid.NewGuid() } })).Status, Is.EqualTo(403));
    }

    [Test]
    public async Task Summary_CountsEveryStatus_AndListsTheTypes()
    {
        await LoginAsync(TestData.Admin);
        await EnqueueAsync(Unique("summary"));

        var summary = await GetJsonAsync("/api/jobs/summary");
        var counts = summary.GetProperty("counts");
        foreach (var status in new[] { "Pending", "Running", "Succeeded", "Failed", "Cancelled" })
            Assert.That(counts.GetProperty(status).GetInt32(), Is.GreaterThanOrEqualTo(0), status);
        Assert.That(counts.GetProperty("Pending").GetInt32(), Is.Positive);
        Assert.That(summary.GetProperty("types").EnumerateArray().Select(t => t.GetString()), Does.Contain("test.record"));

        // Η λίστα δεν κουβαλά payload (μπορεί να είναι μεγάλο)· η λεπτομέρεια ναι
        var item = (await GetJsonAsync("/api/jobs?type=test.record&pageSize=1")).GetProperty("items")[0];
        Assert.That(item.GetProperty("payload").ValueKind, Is.EqualTo(JsonValueKind.Null));
        var detail = await GetJsonAsync($"/api/jobs/{item.GetProperty("id").GetString()}");
        Assert.That(detail.GetProperty("payload").GetString(), Does.StartWith("{"));
    }

    private async Task EnqueueAsync(string key, int failTimes = 0, int? maxAttempts = null)
    {
        var response = await ApiAsync("POST", "/api/test/jobs", new { key, failTimes, maxAttempts });
        Assert.That(response.Status, Is.EqualTo(200), await response.TextAsync());
    }

    private async Task RunJobsAsync() =>
        Assert.That((await ApiAsync("POST", "/api/test/jobs/run")).Status, Is.EqualTo(200));

    private async Task<int> RunsAsync(string key) =>
        (await GetJsonAsync($"/api/test/jobs/runs?key={Uri.EscapeDataString(key)}")).GetProperty("runs").GetInt32();

    private async Task<JsonElement> JobAsync(string key)
    {
        var page = await GetJsonAsync($"/api/jobs?search={Uri.EscapeDataString(key)}&pageSize=100");
        return page.GetProperty("items").EnumerateArray().Single(j => j.GetProperty("idempotencyKey").GetString() == $"test:{key}");
    }

    // Μια δουλειά σε αναμονή: ο runner είναι κλειστός στα tests, οπότε μένει Pending μέχρι το επόμενο /api/test/jobs/run
    private async Task<string> PendingJobIdAsync()
    {
        var key = Unique("later");
        await EnqueueAsync(key);
        return (await JobAsync(key)).GetProperty("id").GetString()!;
    }
}
