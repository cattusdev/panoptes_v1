using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Panoptes_v1.Api;
using Panoptes_v1.Data;
using Panoptes_v1.Modules.Collaboration.Services;
using Panoptes_v1.Modules.Crm.Api;
using Panoptes_v1.Modules.Crm.Entities;
using Panoptes_v1.Services.Access;

namespace Panoptes_v1.Modules.Crm.Services
{
    // Τα πρόσωπα-επαφές. Ένα πρόσωπο μπορεί να είναι επαφή σε πολλούς πελάτες· τα υπόλοιπα των πελατών δεν ενώνονται ποτέ.
    public class PersonService
    {
        public const string EntityType = "person";

        private readonly ApplicationDbContext _db;
        private readonly IRecordAccess<Person> _people;
        private readonly IRecordAccess<Client> _clients;
        private readonly IActivityLog _activity;

        public PersonService(ApplicationDbContext db, IRecordAccess<Person> people, IRecordAccess<Client> clients, IActivityLog activity)
        {
            _db = db;
            _people = people;
            _clients = clients;
            _activity = activity;
        }

        public IQueryable<Person> Visible(ClaimsPrincipal user) => _people.Visible(user);

        private static readonly SortMap<Person> Sort = new SortMap<Person>("name", p => p.Id)
            .Add("name", p => p.FirstName + " " + (p.LastName ?? ""))
            .Add("created", p => p.CreatedAt);

        // search: όνομα (ILIKE) ή ολόκληρο email/τηλέφωνο. clientId: μόνο οι επαφές ενός πελάτη.
        public async Task<PagedResult<PersonListItemDto>> ListAsync(PersonListRequest request, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            var clients = _clients.Visible(user);
            var query = Visible(user).AsNoTracking();
            if (request.ClientId is Guid clientId) query = query.Where(p => p.Clients.Any(cc => cc.ClientId == clientId));
            if (request.SearchTerm is string term)
            {
                var pattern = Like.Contains(term);
                var email = CrmMatching.Email(term);
                var phone = CrmMatching.Phone(term);
                query = query.Where(p =>
                    EF.Functions.ILike(p.FirstName + " " + (p.LastName ?? ""), pattern, Like.EscapeCharacter) ||
                    p.MatchEmails.Contains(email!) ||
                    (phone != null && p.MatchPhones.Contains(phone)));
            }

            var page = await Sort.Apply(query, request.Sort)
                .ToPagedAsync(request, p => new
                {
                    p.Id, p.FirstName, p.LastName, p.ContactMethods,
                    Clients = p.Clients.Where(cc => clients.Any(c => c.Id == cc.ClientId))
                        .OrderBy(cc => cc.Client.DisplayName)
                        .Select(cc => new LinkedClientDto(cc.Client.Id, cc.Client.Ref, cc.Client.DisplayName, cc.Client.ArchivedAt != null))
                        .ToList(),
                }, cancellationToken);

            return page.Map(p => new PersonListItemDto(p.Id, p.FirstName, p.LastName, ClientService.FullName(p.FirstName, p.LastName),
                p.ContactMethods.First(ContactMethodKind.Email),
                p.ContactMethods.First(ContactMethodKind.Mobile, ContactMethodKind.Phone),
                p.Clients));
        }

        public async Task<PersonDto?> GetAsync(Guid id, ClaimsPrincipal user, bool canEdit, CancellationToken cancellationToken = default)
        {
            var clients = _clients.Visible(user);
            var person = await Visible(user).AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => new
                {
                    p.Id, p.FirstName, p.LastName, p.ContactMethods, p.Notes, p.RowVersion,
                    Clients = p.Clients.Where(cc => clients.Any(c => c.Id == cc.ClientId))
                        .OrderByDescending(cc => cc.Client.ArchivedAt == null).ThenBy(cc => cc.Client.DisplayName)
                        .Select(cc => new PersonClientDto(cc.Id, cc.Client.Id, cc.Client.Ref, cc.Client.DisplayName, cc.Client.ArchivedAt != null,
                            cc.Role, cc.IsPrimary, cc.IsBillingContact))
                        .ToList(),
                })
                .FirstOrDefaultAsync(cancellationToken);

            return person == null ? null : new PersonDto(person.Id, person.FirstName, person.LastName, ClientService.FullName(person.FirstName, person.LastName),
                person.ContactMethods.Select(m => m.ToDto()).ToList(), person.Notes, person.Clients, person.RowVersion, canEdit);
        }

        public async Task<Person> CreateAsync(PersonRequest request, CancellationToken cancellationToken = default)
        {
            var person = New(request);
            await _db.SaveChangesAsync(cancellationToken);
            return person;
        }

        // Νέο πρόσωπο στο context (σώζεται με το SaveChanges του καλούντος, π.χ. μαζί με τη σύνδεση στον πελάτη)
        public Person New(PersonRequest request)
        {
            var person = new Person { Id = Guid.NewGuid() };
            Apply(person, request);
            _db.Add(person);
            _activity.Add(new ActivityEntry(EntityType, person.Id, "person.created", new { name = person.FullName }));
            return person;
        }

        public async Task<Person?> UpdateAsync(Guid id, PersonRequest request, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            var person = await Visible(user).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (person == null) return null;

            ClientService.ExpectVersion(_db.Entry(person), request.Version);
            Apply(person, request);
            await _db.SaveChangesAsync(cancellationToken);
            return person;
        }

        // Πρόσωπα που ίσως υπάρχουν ήδη: ίδιο email ή τηλέφωνο, ή όνομα με ομοιότητα ≥ 0.6
        public async Task<List<PersonMatchDto>> FindDuplicatesAsync(PersonRequest request, Guid? excludeId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            var methods = ClientService.ToContactMethods(request.ContactMethods, validate: false);
            var emails = CrmMatching.Emails(methods);
            var phones = CrmMatching.Phones(methods);
            var name = ClientService.FullName(request.FirstName.Trim(), ClientService.Clean(request.LastName));
            const double threshold = CrmMatching.NameSimilarity;

            var rows = await Visible(user).AsNoTracking()
                .Where(p => p.Id != excludeId)
                .Select(p => new
                {
                    p.Id, p.FirstName, p.LastName, p.ContactMethods,
                    NameScore = name.Length == 0 ? 0 : Trigram.Similarity(p.FirstName + " " + (p.LastName ?? ""), name),
                    SameEmail = p.MatchEmails.Any(e => emails.Contains(e)),
                    SamePhone = p.MatchPhones.Any(x => phones.Contains(x)),
                })
                .Where(p => p.NameScore >= threshold || p.SameEmail || p.SamePhone)
                .OrderByDescending(p => p.SameEmail).ThenByDescending(p => p.NameScore).ThenBy(p => p.Id)
                .Take(10)
                .ToListAsync(cancellationToken);

            return rows.Select(p => new PersonMatchDto(p.Id, ClientService.FullName(p.FirstName, p.LastName), p.ContactMethods.First(ContactMethodKind.Email),
                new[] { p.NameScore >= threshold ? "name" : null, p.SameEmail ? "email" : null, p.SamePhone ? "phone" : null }.OfType<string>().ToList())).ToList();
        }

        private static void Apply(Person person, PersonRequest request)
        {
            person.FirstName = request.FirstName.Trim();
            person.LastName = ClientService.Clean(request.LastName);
            person.ContactMethods = ClientService.ToContactMethods(request.ContactMethods, validate: true);
            person.MatchEmails = CrmMatching.Emails(person.ContactMethods);
            person.MatchPhones = CrmMatching.Phones(person.ContactMethods);
            person.Notes = ClientService.Clean(request.Notes);

            if (person.FirstName.Length == 0)
                throw ClientService.Invalid(nameof(PersonRequest.FirstName), "Enter the first name.");
        }
    }
}
