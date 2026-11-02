using System.Text.Json;

namespace Panoptes_v1.E2E;

// Ουρά δουλειών παρασκηνίου: ποτέ διπλή εκτέλεση, επαναλήψεις, αποτυχίες που φαίνονται, επανάληψη/ακύρωση από τον Admin
public class BackgroundJobTests : MediaTest
{
    [Test]
    public async Task SameKey_IsQueuedAndRunOnce()
    {
        await LoginAsync(TestData.Admin);
        var key = Unique("once");

        Assert.That(await EnqueueAsync(key), Is.True, "first request queues the job");
        Assert.That(await EnqueueAsync(key), Is.False, "the same key is not queued again");
        await RunJobsAsync();
        await RunJobsAsync(); // Ούτε δεύτερο "πέρασμα" τη ξανατρέχει

        Assert.That(await RunsAsync(key), Is.EqualTo(1));
        var job = await JobAsync(key);
        Assert.That(job.GetProperty("status").GetString(), Is.EqualTo("Succeeded"));
        Assert.That(job.GetProperty("attempts").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public async Task FailingJob_RetriesThenFails_AndAdminCanRetryIt()
    {
        await LoginAsync(TestData.Admin);
        var key = Unique("flaky");

        // Αποτυγχάνει 2 φορές, έχει 2 προσπάθειες -> Failed με το λάθος
        Assert.That(await EnqueueAsync(key, failTimes: 2, maxAttempts: 2), Is.True);
        await RunJobsAsync();
        var failed = await JobAsync(key);
        Assert.That(failed.GetProperty("status").GetString(), Is.EqualTo("Failed"));
        Assert.That(failed.GetProperty("attempts").GetInt32(), Is.EqualTo(2));
        Assert.That(failed.GetProperty("lastError").GetString(), Does.Contain("Simulated failure 2 of 2"));
        Assert.That(await RunsAsync(key), Is.EqualTo(2));

        // Επανάληψη από τον Admin: ξανά στην ουρά, η 3η φορά πετυχαίνει
        var id = failed.GetProperty("id").GetString();
        Assert.That((await ApiAsync("POST", $"/api/jobs/{id}/retry")).Status, Is.EqualTo(200));
        await RunJobsAsync();
        Assert.That((await JobAsync(key)).GetProperty("status").GetString(), Is.EqualTo("Succeeded"));
        Assert.That(await RunsAsync(key), Is.EqualTo(3));

        // Μια δουλειά που πέτυχε δεν "ξαναπαίζεται"
        var again = await ApiAsync("POST", $"/api/jobs/{id}/retry");
        Assert.That(again.Status, Is.EqualTo(409));
        Assert.That(await again.TextAsync(), Does.Contain("Only failed or cancelled jobs can be retried."));
    }

    [Test]
    public async Task CancelledJob_NeverRuns()
    {
        await LoginAsync(TestData.Admin);
        var key = Unique("cancel");
        Assert.That(await EnqueueAsync(key), Is.True);

        var id = (await JobAsync(key)).GetProperty("id").GetString();
        Assert.That((await ApiAsync("POST", $"/api/jobs/{id}/cancel")).Status, Is.EqualTo(200));
        await RunJobsAsync();

        Assert.That(await RunsAsync(key), Is.EqualTo(0));
        Assert.That((await JobAsync(key)).GetProperty("status").GetString(), Is.EqualTo("Cancelled"));
    }

    [Test]
    public async Task EmailJob_SendsOnce_EvenWhenQueuedTwice()
    {
        await LoginAsync(TestData.Admin);
        var to = $"{Guid.NewGuid():N}@example.test";
        var key = Unique("reminder");
        var message = new { to, subject = "Renewal reminder", html = "<p>Your hosting renews soon.</p>" };

        foreach (var _ in Enumerable.Range(0, 2))
            Assert.That((await ApiAsync("POST", $"/api/test/jobs/email?key={Uri.EscapeDataString(key)}", message)).Status, Is.EqualTo(200));
        await RunJobsAsync();

        var email = await Mailbox.WaitForAsync(to, "Renewal reminder");
        Assert.That(email.Html, Does.Contain("Your hosting renews soon."));
        Assert.That(Mailbox.For(to), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task JobsApi_IsAdminOnly()
    {
        await LoginAsync(TestData.MediaManager);
        Assert.That((await ApiAsync("GET", "/api/jobs")).Status, Is.EqualTo(403));
    }

    private async Task<bool> EnqueueAsync(string key, int failTimes = 0, int? maxAttempts = null, bool createFolder = false)
    {
        var response = await ApiAsync("POST", "/api/test/jobs", new { key, failTimes, maxAttempts, createFolder });
        Assert.That(response.Status, Is.EqualTo(200), await response.TextAsync());
        return (await response.JsonAsync())!.Value.GetProperty("created").GetBoolean();
    }

    protected async Task RunJobsAsync() =>
        Assert.That((await ApiAsync("POST", "/api/test/jobs/run")).Status, Is.EqualTo(200));

    private async Task<int> RunsAsync(string key) =>
        (await GetJsonAsync($"/api/test/jobs/runs?key={Uri.EscapeDataString(key)}")).GetProperty("runs").GetInt32();

    private async Task<JsonElement> JobAsync(string key)
    {
        var page = await GetJsonAsync("/api/jobs?type=test.record&pageSize=100");
        return page.GetProperty("items").EnumerateArray().Single(j => j.GetProperty("idempotencyKey").GetString() == $"test:{key}");
    }
}
