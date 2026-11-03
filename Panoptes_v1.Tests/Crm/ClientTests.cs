using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Panoptes_v1.Api;
using Panoptes_v1.Constants;
using Panoptes_v1.Modules.Collaboration.Entities;
using Panoptes_v1.Modules.Collaboration.Services;
using Panoptes_v1.Modules.Crm.Api;
using Panoptes_v1.Modules.Crm.Entities;
using Panoptes_v1.Modules.Crm.Services;
using Panoptes_v1.Services;
using Panoptes_v1.Services.Search;

namespace Panoptes_v1.Tests.Crm;

// Πελάτες, επαφές και διπλότυπα (ARCHITECTURE §4.1, CRM-01…06)
public class ClientTests : DomainTest
{
    private ClaimsPrincipal _user = null!;

    [SetUp]
    public void SignInManager() =>
        _user = SignInAs("crm-manager", Permissions.Clients.View, Permissions.Clients.Create, Permissions.Clients.Edit, Permissions.Clients.Archive);

    private static string NewTaxId() => Random.Shared.NextInt64(100_000_000, 999_999_999).ToString();

    // Χωρίς δοσμένους τρόπους επικοινωνίας: ένα μοναδικό email (ο πελάτης θέλει τουλάχιστον email ή τηλέφωνο)
    private static ClientRequest NewClient(string? name = null, string? taxId = null, params ContactMethodRequest[] methods) => new()
    {
        Kind = ClientKind.Organization,
        DisplayName = name ?? Unique("Client"),
        TaxId = taxId,
        ContactMethods = methods.Length > 0 ? [.. methods] : [Email($"{Guid.NewGuid():N}@example.com")],
    };

    private static ContactMethodRequest Email(string value) => new() { Kind = ContactMethodKind.Email, Value = value };

    private static ContactMethodRequest Phone(string value) => new() { Kind = ContactMethodKind.Phone, Value = value };

    private Task<T> WithClientsAsync<T>(Func<ClientService, Task<T>> action) =>
        WithScopeAsync(services => action(services.GetRequiredService<ClientService>()));

    private Task<Client> CreateAsync(ClientRequest request) => WithClientsAsync(clients => clients.CreateAsync(request, _user));

    private Task<List<ActivityEvent>> ActivityAsync(Guid clientId) =>
        WithDbAsync(db => db.Set<ActivityEvent>().AsNoTracking().Where(e => e.ClientId == clientId).OrderBy(e => e.OccurredAt).ToListAsync());

    [Test]
    public async Task Create_GivesAReference_AndRecordsWhoDidIt()
    {
        var client = await CreateAsync(NewClient(taxId: "EL " + NewTaxId()));

        Assert.That(client.Ref, Does.Match(@"^CL-\d{4,}$"));
        Assert.That(client.TaxIdNormalized, Does.Match(@"^\d{9}$"));
        var activity = await ActivityAsync(client.Id);
        Assert.That(activity.Select(e => e.Verb), Is.EqualTo(new[] { "client.created" }));
        Assert.That(activity[0].ActorId, Is.EqualTo("crm-manager"));
    }

    // Ίδιος ΑΦΜ = σκληρή προειδοποίηση: σταματά, εκτός αν δοθεί αιτιολογία (που μένει στο ιστορικό)
    [Test]
    public async Task SameTaxId_IsRefusedWithoutAReason_AndKeptWithOne()
    {
        var taxId = NewTaxId();
        var first = await CreateAsync(NewClient(taxId: taxId));

        var refused = Assert.ThrowsAsync<BusinessRuleException>(() => CreateAsync(NewClient(taxId: $"GR {taxId[..3]}.{taxId[3..6]}.{taxId[6..]}")));
        Assert.That(refused!.Code, Is.EqualTo("crm.duplicateTaxId"));
        Assert.That(refused.Field, Is.EqualTo(nameof(ClientRequest.TaxId)));

        var request = NewClient(taxId: taxId);
        request.DuplicateReason = "Branch with its own balance";
        var second = await CreateAsync(request);

        var confirmation = (await ActivityAsync(second.Id)).Single(e => e.Verb == "client.duplicateConfirmed");
        using var data = JsonDocument.Parse(confirmation.Data);
        Assert.That(data.RootElement.GetProperty("reason").GetString(), Is.EqualTo("Branch with its own balance"));
        Assert.That(data.RootElement.GetProperty("matches")[0].GetProperty("ref").GetString(), Is.EqualTo(first.Ref));
    }

    [Test]
    public async Task FindDuplicates_FlagsSimilarNames_AndTheSameEmailOrPhone()
    {
        var stem = Unique("Βήτα Λογιστική");
        var existing = await CreateAsync(NewClient($"{stem} ΙΚΕ", null, Email($"info@{stem}.gr"), Phone("+30 210 555 0101")));

        var matches = await WithClientsAsync(clients => clients.FindDuplicatesAsync(new ClientDuplicateCheckRequest
        {
            DisplayName = $"{stem} IKE",
            ContactMethods = [Email($"INFO@{stem}.gr "), Phone("0030 2105550101")],
        }, _user));

        var match = matches.Single(m => m.Id == existing.Id);
        Assert.That(match.Reasons, Is.EquivalentTo(new[] { "name", "email", "phone" }));

        var unrelated = await WithClientsAsync(clients => clients.FindDuplicatesAsync(new ClientDuplicateCheckRequest { DisplayName = Unique("Gamma") }, _user));
        Assert.That(unrelated.Select(m => m.Id), Does.Not.Contain(existing.Id));
    }

    // Ο ΑΦΜ ελέγχεται μόνο όταν αλλάζει: ένα ήδη επιβεβαιωμένο διπλότυπο δεν ξαναρωτιέται σε κάθε αλλαγή
    [Test]
    public async Task ChangingTheTaxIdToAnotherClients_NeedsAReason()
    {
        var taxId = NewTaxId();
        var other = await CreateAsync(NewClient(taxId: taxId));
        var client = await CreateAsync(NewClient());

        var change = NewClient(client.DisplayName, taxId);
        change.Version = client.RowVersion;
        var refused = Assert.ThrowsAsync<BusinessRuleException>(() => WithClientsAsync(clients => clients.UpdateAsync(client.Id, change, _user)));
        Assert.That(refused!.Code, Is.EqualTo("crm.duplicateTaxId"));

        change.DuplicateReason = "Same company, second billing entity";
        var updated = await WithClientsAsync(clients => clients.UpdateAsync(client.Id, change, _user));

        var rename = NewClient("Renamed " + client.DisplayName, taxId);
        rename.Version = updated!.RowVersion;
        var renamed = await WithClientsAsync(clients => clients.UpdateAsync(client.Id, rename, _user));
        Assert.That(renamed!.DisplayName, Does.StartWith("Renamed"));

        var verbs = (await ActivityAsync(client.Id)).Select(e => e.Verb);
        Assert.That(verbs, Is.EquivalentTo(new[] { "client.created", "client.updated", "client.duplicateConfirmed", "client.updated" }));
        Assert.That(other.Id, Is.Not.EqualTo(client.Id));
    }

    [Test]
    public async Task Update_RecordsOnlyTheFieldsThatChanged()
    {
        var client = await CreateAsync(NewClient(null, null, Email("a@example.com")));

        var same = NewClient(client.DisplayName, null, Email("a@example.com"));
        same.Version = client.RowVersion;
        await WithClientsAsync(clients => clients.UpdateAsync(client.Id, same, _user));

        var change = NewClient(client.DisplayName, null, Email("b@example.com"));
        change.TaxOffice = "ΔΟΥ Αθηνών";
        change.Version = client.RowVersion;
        await WithClientsAsync(clients => clients.UpdateAsync(client.Id, change, _user));

        var updates = (await ActivityAsync(client.Id)).Where(e => e.Verb == "client.updated").ToList();
        Assert.That(updates, Has.Count.EqualTo(1));
        using var data = JsonDocument.Parse(updates[0].Data);
        Assert.That(data.RootElement.GetProperty("fields").EnumerateArray().Select(f => f.GetString()), Is.EquivalentTo(new[] { "taxOffice", "contactMethods" }));
    }

    [Test]
    public async Task StaleVersion_IsAConflict()
    {
        var client = await CreateAsync(NewClient());
        var first = NewClient("First " + client.DisplayName);
        first.Version = client.RowVersion;
        await WithClientsAsync(clients => clients.UpdateAsync(client.Id, first, _user));

        var stale = NewClient("Second " + client.DisplayName);
        stale.Version = client.RowVersion;
        Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => WithClientsAsync(clients => clients.UpdateAsync(client.Id, stale, _user)));
    }

    // CRM-06: η αρχειοθέτηση δεν αλλάζει την κατάσταση (Active/Inactive), και το Unarchive τον γυρίζει όπως ήταν
    [Test]
    public async Task Archive_KeepsTheStatus_AndUnarchiveRestoresIt()
    {
        var request = NewClient();
        request.Status = ClientStatus.Inactive;
        var client = await CreateAsync(request);

        var archived = await WithClientsAsync(clients => clients.ArchiveAsync(client.Id, client.RowVersion, _user));
        Assert.That(archived!.ArchivedAt, Is.Not.Null);
        Assert.That(archived.ArchivedById, Is.EqualTo("crm-manager"));
        Assert.That(archived.Status, Is.EqualTo(ClientStatus.Inactive));

        // Δεύτερη φορά: τίποτα (ούτε νέα γραμμή στο ιστορικό)
        await WithClientsAsync(clients => clients.ArchiveAsync(client.Id, null, _user));

        var restored = await WithClientsAsync(clients => clients.UnarchiveAsync(client.Id, null, _user));
        Assert.That(restored!.ArchivedAt, Is.Null);
        Assert.That(restored.Status, Is.EqualTo(ClientStatus.Inactive));
        Assert.That((await ActivityAsync(client.Id)).Select(e => e.Verb), Is.EqualTo(new[] { "client.created", "client.archived", "client.unarchived" }));
    }

    // CRM-01/02: ένα πρόσωπο σε δύο πελάτες = δύο ξεχωριστές σχέσεις. Ο ένας πελάτης δείχνει τον άλλον μόνο μέσω της επαφής.
    [Test]
    public async Task SharedContact_BelongsToBothClients_WithoutJoiningThem()
    {
        var beta = await CreateAsync(NewClient(Unique("Beta")));
        var gamma = await CreateAsync(NewClient(Unique("Gamma")));

        await WithScopeAsync(async services =>
        {
            var clients = services.GetRequiredService<ClientService>();
            var people = services.GetRequiredService<PersonService>();
            await clients.AddContactAsync(beta.Id, new AddContactRequest
            {
                Person = new PersonRequest { FirstName = "Maria", LastName = Unique("P"), ContactMethods = [Email($"{Guid.NewGuid():N}@beta.gr")] },
                Role = "CEO",
                IsPrimary = true,
            }, _user, people);
        });
        var maria = (await WithClientsAsync(clients => clients.ContactsAsync(beta.Id, _user)))!.Single();
        await WithScopeAsync(services => services.GetRequiredService<ClientService>()
            .AddContactAsync(gamma.Id, new AddContactRequest { PersonId = maria.PersonId, Role = "Owner" }, _user, services.GetRequiredService<PersonService>()));

        var betaContacts = (await WithClientsAsync(clients => clients.ContactsAsync(beta.Id, _user)))!;
        var gammaContacts = (await WithClientsAsync(clients => clients.ContactsAsync(gamma.Id, _user)))!;
        Assert.That(betaContacts.Single().AlsoContactFor.Select(c => c.Id), Is.EqualTo(new[] { gamma.Id }));
        Assert.That(gammaContacts.Single().AlsoContactFor.Select(c => c.Id), Is.EqualTo(new[] { beta.Id }));
        Assert.That(betaContacts.Single().Role, Is.EqualTo("CEO"));
        Assert.That(gammaContacts.Single().Role, Is.EqualTo("Owner"));
        Assert.That(gammaContacts.Single().IsPrimary, Is.False);

        // Ξανά στον ίδιο πελάτη: όχι
        var again = Assert.ThrowsAsync<BusinessRuleException>(() => WithScopeAsync(services => services.GetRequiredService<ClientService>()
            .AddContactAsync(gamma.Id, new AddContactRequest { PersonId = maria.PersonId }, _user, services.GetRequiredService<PersonService>())));
        Assert.That(again!.Code, Is.EqualTo("crm.contactExists"));

        // Αφαίρεση από τον έναν: η σχέση φεύγει, το πρόσωπο και η άλλη σχέση μένουν
        var removed = await WithClientsAsync(clients => clients.RemoveContactAsync(gamma.Id, gammaContacts.Single().Id, _user));
        Assert.That(removed, Is.True);
        Assert.That(await WithClientsAsync(clients => clients.ContactsAsync(gamma.Id, _user)), Is.Empty);
        Assert.That((await WithClientsAsync(clients => clients.ContactsAsync(beta.Id, _user)))!.Single().AlsoContactFor, Is.Empty);
    }

    [Test]
    public async Task ANewPrimaryContact_ReplacesTheOldOne()
    {
        var client = await CreateAsync(NewClient());
        async Task AddAsync(string name, bool primary) => await WithScopeAsync(services => services.GetRequiredService<ClientService>()
            .AddContactAsync(client.Id, new AddContactRequest { Person = new PersonRequest { FirstName = name }, IsPrimary = primary }, _user,
                services.GetRequiredService<PersonService>()));

        await AddAsync("Anna", primary: true);
        await AddAsync("Babis", primary: true);
        var contacts = (await WithClientsAsync(clients => clients.ContactsAsync(client.Id, _user)))!;
        Assert.That(contacts.Where(c => c.IsPrimary).Select(c => c.FirstName), Is.EqualTo(new[] { "Babis" }));

        var anna = contacts.Single(c => c.FirstName == "Anna");
        await WithClientsAsync(clients => clients.UpdateContactAsync(client.Id, anna.Id,
            new UpdateContactRequest { IsPrimary = true, IsBillingContact = true, Version = anna.Version }, _user));
        contacts = (await WithClientsAsync(clients => clients.ContactsAsync(client.Id, _user)))!;
        Assert.That(contacts.Where(c => c.IsPrimary).Select(c => c.FirstName), Is.EqualTo(new[] { "Anna" }));
        Assert.That(contacts[0].IsBillingContact, Is.True);
    }

    // Χωρίς Clients.View: ούτε λίστα, ούτε λεπτομέρεια, ούτε επαφές, ούτε αναζήτηση
    [Test]
    public async Task WithoutClientsView_NothingIsVisible()
    {
        var name = Unique("Hidden");
        var client = await CreateAsync(NewClient(name));
        var outsider = SignInAs("outsider", Permissions.Media.View);

        await WithScopeAsync(async services =>
        {
            var clients = services.GetRequiredService<ClientService>();
            Assert.That(await clients.Visible(outsider).CountAsync(), Is.Zero);
            Assert.That(await clients.FindAsync(client.Id, outsider), Is.Null);
            Assert.That(await clients.ContactsAsync(client.Id, outsider), Is.Null);
            Assert.That(await clients.UpdateAsync(client.Id, NewClient("Changed"), outsider), Is.Null);
            Assert.That(await clients.ArchiveAsync(client.Id, null, outsider), Is.Null);
            Assert.That(await services.GetRequiredService<SearchService>().SearchAsync(name, outsider), Is.Empty);
        });
    }

    [Test]
    public async Task Search_FindsClientsByNameReferenceAndTaxId_AndPeopleByEmail()
    {
        var name = Unique("Δέλτα 50%");
        var taxId = NewTaxId();
        var client = await CreateAsync(NewClient(name, taxId));
        var email = $"{Guid.NewGuid():N}@delta.gr";
        await WithScopeAsync(services => services.GetRequiredService<ClientService>().AddContactAsync(client.Id,
            new AddContactRequest { Person = new PersonRequest { FirstName = "Nikos", ContactMethods = [Email(email)] } }, _user,
            services.GetRequiredService<PersonService>()));

        async Task<IReadOnlyList<SearchGroup>> SearchAsync(string q) =>
            await WithScopeAsync(services => services.GetRequiredService<SearchService>().SearchAsync(q, _user));

        foreach (var query in new[] { name, client.Ref.ToLowerInvariant(), $"EL{taxId}", "50%" })
        {
            var clients = (await SearchAsync(query)).Single(g => g.Type == "client").Hits;
            Assert.That(clients.Select(h => h.Id), Does.Contain(client.Id), query);
        }

        var people = (await SearchAsync(email.ToUpperInvariant())).Single(g => g.Type == "person").Hits;
        Assert.That(people.Single().Subtitle, Is.EqualTo(name));

        // Το % ψάχνεται ως χαρακτήρας: "0%" υπάρχει, "5_%" όχι
        Assert.That(await SearchAsync("Δέλτα 5_%"), Is.Empty);
    }

    [Test]
    public async Task Activity_ShowsOnlyThisClientsEvents()
    {
        var first = await CreateAsync(NewClient());
        var second = await CreateAsync(NewClient());

        var page = await WithScopeAsync(services => services.GetRequiredService<IActivityLog>().ForClientAsync(first.Id, _user, new ListRequest()));

        Assert.That(page.Items.Select(i => i.EntityId), Is.All.EqualTo(first.Id));
        Assert.That(page.Items.Select(i => i.Verb), Is.EqualTo(new[] { "client.created" }));
        Assert.That(page.Items[0].Actor?.Id, Is.EqualTo("crm-manager"));
        Assert.That(second.Id, Is.Not.EqualTo(first.Id));
    }

    [Test]
    public void InvalidEmail_IsAFieldError()
    {
        var error = Assert.ThrowsAsync<BusinessRuleException>(() => CreateAsync(NewClient(null, null, Email("not-an-email"))));

        Assert.That(error!.StatusCode, Is.EqualTo(400));
        Assert.That(error.Field, Is.EqualTo(nameof(ClientRequest.ContactMethods)));
    }

    [TestCase("EL 099 999 999", "099999999")]
    [TestCase("gr099.999.999", "099999999")]
    [TestCase("DE123456789", "DE123456789")]
    [TestCase("  ", null)]
    public void TaxIds_AreNormalized(string input, string? expected) =>
        Assert.That(CrmMatching.TaxId(input), Is.EqualTo(expected));

    // Όπως αποθηκεύονται (E.164): χωρίς "+" ο αριθμός διαβάζεται ως ελληνικός
    [TestCase("+30 210 123 4567", "+302101234567")]
    [TestCase("0030 6912345678", "+306912345678")]
    [TestCase("210-1234567", "+302101234567")]
    [TestCase("+44 20 7946 0958", "+442079460958")]
    [TestCase("123", null)]
    [TestCase("210 12", null)]
    public void Phones_AreNormalized(string input, string? expected) =>
        Assert.That(CrmMatching.Phone(input), Is.EqualTo(expected));

    [Test]
    public async Task Phones_AreStoredWithTheirCountryCode()
    {
        var client = await CreateAsync(NewClient(null, null, Phone("210 123 4567"), new ContactMethodRequest { Kind = ContactMethodKind.Mobile, Value = "+44 7911 123456" }));

        Assert.That(client.ContactMethods.Select(m => m.Value), Is.EqualTo(new[] { "+302101234567", "+447911123456" }));
        Assert.That(client.MatchPhones, Is.EquivalentTo(new[] { "+302101234567", "+447911123456" }));
    }

    [Test]
    public void InvalidPhone_IsAFieldError()
    {
        var error = Assert.ThrowsAsync<BusinessRuleException>(() => CreateAsync(NewClient(null, null, Email("a@example.com"), Phone("210 12"))));

        Assert.That(error!.StatusCode, Is.EqualTo(400));
        Assert.That(error.Field, Is.EqualTo(nameof(ClientRequest.ContactMethods)));
    }

    // Ένας πελάτης χωρίς email ή τηλέφωνο δεν αποθηκεύεται (ένα site μόνο του δεν αρκεί)
    [Test]
    public void Client_NeedsAnEmailOrAPhone()
    {
        var error = Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateAsync(NewClient(null, null, new ContactMethodRequest { Kind = ContactMethodKind.Website, Value = "beta.example" })));

        Assert.That(error!.Field, Is.EqualTo(nameof(ClientRequest.ContactMethods)));
    }

    // Ιδιώτης: όνομα και επώνυμο, και το όνομα του πελάτη βγαίνει από αυτά
    [Test]
    public async Task Individual_HasAFirstAndLastName()
    {
        var lastName = Unique("Papadopoulou");
        var request = NewClient();
        request.Kind = ClientKind.Individual;
        request.DisplayName = null;
        request.FirstName = " Maria ";
        request.LastName = lastName;

        var client = await CreateAsync(request);

        Assert.That((client.FirstName, client.LastName, client.DisplayName), Is.EqualTo(("Maria", lastName, $"Maria {lastName}")));

        request.LastName = " ";
        var error = Assert.ThrowsAsync<BusinessRuleException>(() => CreateAsync(request));
        Assert.That(error!.Field, Is.EqualTo(nameof(ClientRequest.LastName)));
    }
}
