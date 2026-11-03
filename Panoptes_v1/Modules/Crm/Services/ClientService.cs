using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Panoptes_v1.Data;
using Panoptes_v1.Models;
using Panoptes_v1.Modules.Collaboration.Services;
using Panoptes_v1.Modules.Crm.Api;
using Panoptes_v1.Modules.Crm.Entities;
using Panoptes_v1.Services;
using Panoptes_v1.Services.Access;
using Panoptes_v1.Services.Media;

namespace Panoptes_v1.Modules.Crm.Services
{
    // Πελάτες και οι επαφές τους (ARCHITECTURE §4.1). Κάθε ανάγνωση ξεκινά από το IRecordAccess<Client>.
    // Ο έλεγχος permission (Create/Edit/Archive) γίνεται στον controller· εδώ οι κανόνες και τα transactions.
    // null από μια μέθοδο = ο πελάτης δεν υπάρχει ή ο χρήστης δεν τον βλέπει (404, ίδιο μήνυμα και για τα δύο).
    public partial class ClientService
    {
        public const string EntityType = "client";

        // Παράγονται από άλλα πεδία: δεν είναι "αλλαγές" του χρήστη στο ιστορικό
        private static readonly HashSet<string> DerivedFields =
            [nameof(Client.TaxIdNormalized), nameof(Client.MatchEmails), nameof(Client.MatchPhones)];

        private readonly ApplicationDbContext _db;
        private readonly IRecordAccess<Client> _clients;
        private readonly IRecordAccess<Person> _people;
        private readonly ReferenceGenerator _references;
        private readonly IActivityLog _activity;

        public ClientService(ApplicationDbContext db, IRecordAccess<Client> clients, IRecordAccess<Person> people, ReferenceGenerator references, IActivityLog activity)
        {
            _db = db;
            _clients = clients;
            _people = people;
            _references = references;
            _activity = activity;
        }

        public IQueryable<Client> Visible(ClaimsPrincipal user) => _clients.Visible(user);

        public Task<Client?> FindAsync(Guid id, ClaimsPrincipal user, CancellationToken cancellationToken = default) =>
            Visible(user).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        // Νέος πελάτης με τον επόμενο κωδικό (CL-0042). Origin: Live από το UI, Historical από το onboarding.
        public async Task<Client> CreateAsync(ClientRequest request, ClaimsPrincipal user, RecordOrigin origin = RecordOrigin.Live, string? sourceRef = null,
            CancellationToken cancellationToken = default)
        {
            var client = new Client { Id = Guid.NewGuid(), Origin = origin, SourceRef = sourceRef };
            Apply(client, request);
            var confirmed = await ConfirmTaxIdDuplicatesAsync(client, request.DuplicateReason, user, cancellationToken);

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            client.Ref = await _references.NextAsync(ReferenceFormats.Client, cancellationToken);
            _db.Add(client);
            _activity.Add(new ActivityEntry(EntityType, client.Id, "client.created", new { client.Ref, name = client.DisplayName }, client.Id));
            AddDuplicateActivity(client, confirmed, request.DuplicateReason);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return client;
        }

        public async Task<Client?> UpdateAsync(Guid id, ClientRequest request, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            var client = await FindAsync(id, user, cancellationToken);
            if (client == null) return null;

            ExpectVersion(_db.Entry(client), request.Version);
            var previousTaxId = client.TaxIdNormalized;
            Apply(client, request);
            // Μόνο όταν αλλάζει ο ΑΦΜ: ένα παλιό, ήδη επιβεβαιωμένο διπλότυπο δεν ξαναρωτιέται σε κάθε αλλαγή
            var confirmed = client.TaxIdNormalized != previousTaxId
                ? await ConfirmTaxIdDuplicatesAsync(client, request.DuplicateReason, user, cancellationToken)
                : [];

            var changed = ChangedFields(client);
            if (changed.Count == 0) return client;

            _activity.Add(new ActivityEntry(EntityType, client.Id, "client.updated", new { fields = changed }, client.Id));
            AddDuplicateActivity(client, confirmed, request.DuplicateReason);
            await _db.SaveChangesAsync(cancellationToken);
            return client;
        }

        // Αρχειοθέτηση: μια κατάσταση, όχι διαγραφή. Δεν ακυρώνει τίποτα (υπηρεσίες, χρεώσεις, projects μένουν όπως είναι, CRM-06).
        public async Task<Client?> ArchiveAsync(Guid id, uint? version, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            var client = await FindAsync(id, user, cancellationToken);
            if (client == null || client.ArchivedAt != null) return client;

            ExpectVersion(_db.Entry(client), version);
            client.ArchivedAt = DateTime.UtcNow;
            client.ArchivedById = user.FindFirstValue(ClaimTypes.NameIdentifier);
            _activity.Add(new ActivityEntry(EntityType, client.Id, "client.archived", null, client.Id));
            await _db.SaveChangesAsync(cancellationToken);
            return client;
        }

        public async Task<Client?> UnarchiveAsync(Guid id, uint? version, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            var client = await FindAsync(id, user, cancellationToken);
            if (client == null || client.ArchivedAt == null) return client;

            ExpectVersion(_db.Entry(client), version);
            client.ArchivedAt = null;
            client.ArchivedById = null;
            _activity.Add(new ActivityEntry(EntityType, client.Id, "client.unarchived", null, client.Id));
            await _db.SaveChangesAsync(cancellationToken);
            return client;
        }

        // --- Διπλότυπα ---

        // Πιθανά διπλότυπα (ARCHITECTURE §4.1): ίδιος ΑΦΜ (σκληρή προειδοποίηση), όνομα με ομοιότητα ≥ 0.6, ίδιο email ή τηλέφωνο
        public async Task<List<ClientMatchDto>> FindDuplicatesAsync(ClientDuplicateCheckRequest request, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            var methods = ToContactMethods(request.ContactMethods, validate: false);
            return await FindDuplicatesAsync(
                Clean(request.DisplayName), Clean(request.LegalName), CrmMatching.TaxId(request.TaxId),
                CrmMatching.Emails(methods), CrmMatching.Phones(methods), request.ExcludeId, user, cancellationToken);
        }

        private async Task<List<ClientMatchDto>> FindDuplicatesAsync(string? name, string? legalName, string? taxId, List<string> emails, List<string> phones,
            Guid? excludeId, ClaimsPrincipal user, CancellationToken cancellationToken)
        {
            if (name == null && legalName == null && taxId == null && emails.Count == 0 && phones.Count == 0) return [];

            const double threshold = CrmMatching.NameSimilarity;
            var names = new[] { name, legalName }.OfType<string>().Distinct().ToList();
            var first = names.ElementAtOrDefault(0);
            var second = names.ElementAtOrDefault(1);

            var rows = await Visible(user).AsNoTracking()
                .Where(c => c.Id != excludeId)
                .Select(c => new
                {
                    c.Id, c.Ref, c.DisplayName, c.LegalName, c.TaxId, c.TaxIdNormalized, c.ArchivedAt,
                    SameTaxId = taxId != null && c.TaxIdNormalized == taxId,
                    NameScore = Math.Max(
                        Math.Max(first == null ? 0 : Trigram.Similarity(c.DisplayName, first), first == null ? 0 : Trigram.Similarity(c.LegalName, first)),
                        Math.Max(second == null ? 0 : Trigram.Similarity(c.DisplayName, second), second == null ? 0 : Trigram.Similarity(c.LegalName, second))),
                    SameEmail = c.MatchEmails.Any(e => emails.Contains(e)),
                    SamePhone = c.MatchPhones.Any(p => phones.Contains(p)),
                })
                .Where(c => c.SameTaxId || c.NameScore >= threshold || c.SameEmail || c.SamePhone)
                .OrderByDescending(c => c.SameTaxId).ThenByDescending(c => c.NameScore).ThenBy(c => c.Ref)
                .Take(10)
                .ToListAsync(cancellationToken);

            return rows.Select(c => new ClientMatchDto(c.Id, c.Ref, c.DisplayName, c.LegalName, c.TaxId, c.ArchivedAt != null,
                new[]
                {
                    c.SameTaxId ? "taxId" : null,
                    c.NameScore >= threshold ? "name" : null,
                    c.SameEmail ? "email" : null,
                    c.SamePhone ? "phone" : null,
                }.OfType<string>().ToList())).ToList();
        }

        // Ίδιος ΑΦΜ με άλλον πελάτη: σταματά, εκτός αν ο χρήστης έδωσε αιτιολογία (π.χ. υποκατάστημα με ίδιο ΑΦΜ)
        private async Task<List<ClientMatchDto>> ConfirmTaxIdDuplicatesAsync(Client client, string? reason, ClaimsPrincipal user, CancellationToken cancellationToken)
        {
            if (client.TaxIdNormalized == null) return [];
            var matches = await Visible(user).AsNoTracking()
                .Where(c => c.Id != client.Id && c.TaxIdNormalized == client.TaxIdNormalized)
                .OrderBy(c => c.Ref)
                .Select(c => new ClientMatchDto(c.Id, c.Ref, c.DisplayName, c.LegalName, c.TaxId, c.ArchivedAt != null, new[] { "taxId" }))
                .ToListAsync(cancellationToken);
            if (matches.Count == 0 || !string.IsNullOrWhiteSpace(reason)) return matches;

            throw new BusinessRuleException("crm.duplicateTaxId", "Another client already has this tax ID: {0}.", $"{matches[0].DisplayName} ({matches[0].Ref})")
            {
                Field = nameof(ClientRequest.TaxId),
                Details = new { matches },
            };
        }

        private void AddDuplicateActivity(Client client, List<ClientMatchDto> confirmed, string? reason)
        {
            if (confirmed.Count == 0) return;
            _activity.Add(new ActivityEntry(EntityType, client.Id, "client.duplicateConfirmed",
                new { reason = reason!.Trim(), matches = confirmed.Select(m => new { m.Id, m.Ref, name = m.DisplayName }) }, client.Id));
        }

        // --- Επαφές ---

        public async Task<List<ClientContactDto>?> ContactsAsync(Guid clientId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            if (!await Visible(user).AnyAsync(c => c.Id == clientId, cancellationToken)) return null;
            var clients = Visible(user);

            var rows = await _db.Set<ClientContact>().AsNoTracking()
                .Where(cc => cc.ClientId == clientId)
                .OrderByDescending(cc => cc.IsPrimary).ThenByDescending(cc => cc.IsBillingContact)
                .ThenBy(cc => cc.Person.FirstName).ThenBy(cc => cc.Person.LastName).ThenBy(cc => cc.Id)
                .Select(cc => new
                {
                    cc.Id, cc.PersonId, cc.Person.FirstName, cc.Person.LastName, cc.Role, cc.IsPrimary, cc.IsBillingContact, cc.Person.ContactMethods, cc.RowVersion,
                    Others = cc.Person.Clients
                        .Where(o => o.ClientId != clientId && clients.Any(c => c.Id == o.ClientId))
                        .OrderBy(o => o.Client.DisplayName)
                        .Select(o => new LinkedClientDto(o.Client.Id, o.Client.Ref, o.Client.DisplayName, o.Client.ArchivedAt != null))
                        .ToList(),
                })
                .ToListAsync(cancellationToken);

            return rows.Select(r => new ClientContactDto(r.Id, r.PersonId, r.FirstName, r.LastName, FullName(r.FirstName, r.LastName), r.Role,
                r.IsPrimary, r.IsBillingContact, r.ContactMethods.Select(m => m.ToDto()).ToList(), r.Others, r.RowVersion)).ToList();
        }

        // Ένα υπάρχον πρόσωπο (PersonId) ή ένα νέο (Person) ως επαφή του πελάτη
        public async Task<ClientContact?> AddContactAsync(Guid clientId, AddContactRequest request, ClaimsPrincipal user, PersonService people,
            CancellationToken cancellationToken = default)
        {
            var client = await FindAsync(clientId, user, cancellationToken);
            if (client == null) return null;

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            Person person;
            if (request.PersonId is Guid personId)
            {
                person = await _people.Visible(user).FirstOrDefaultAsync(p => p.Id == personId, cancellationToken)
                    ?? throw new BusinessRuleException("crm.personNotFound", "This person no longer exists.") { StatusCode = StatusCodes.Status404NotFound };
                if (await _db.Set<ClientContact>().AnyAsync(cc => cc.ClientId == clientId && cc.PersonId == personId, cancellationToken))
                    throw new BusinessRuleException("crm.contactExists", "{0} is already a contact of this client.", person.FullName);
            }
            else if (request.Person != null)
            {
                person = people.New(request.Person);
            }
            else
            {
                throw new BusinessRuleException("crm.personRequired", "Choose a person or enter a new one.")
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Field = nameof(AddContactRequest.PersonId),
                };
            }

            if (request.IsPrimary) await ClearPrimaryAsync(clientId, exceptId: null, cancellationToken);

            var contact = new ClientContact
            {
                Id = Guid.NewGuid(),
                ClientId = clientId,
                PersonId = person.Id,
                Role = Clean(request.Role),
                IsPrimary = request.IsPrimary,
                IsBillingContact = request.IsBillingContact,
            };
            _db.Add(contact);
            _activity.Add(new ActivityEntry(EntityType, clientId, "client.contactAdded",
                new { personId = person.Id, person = person.FullName, role = contact.Role, primary = contact.IsPrimary }, clientId));
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return contact;
        }

        public async Task<ClientContact?> UpdateContactAsync(Guid clientId, Guid contactId, UpdateContactRequest request, ClaimsPrincipal user,
            CancellationToken cancellationToken = default)
        {
            var contact = await FindContactAsync(clientId, contactId, user, cancellationToken);
            if (contact == null) return null;

            ExpectVersion(_db.Entry(contact), request.Version);
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            if (request.IsPrimary && !contact.IsPrimary) await ClearPrimaryAsync(clientId, contact.Id, cancellationToken);

            contact.Role = Clean(request.Role);
            contact.IsPrimary = request.IsPrimary;
            contact.IsBillingContact = request.IsBillingContact;
            var changed = ChangedFields(contact);
            if (changed.Count > 0)
            {
                _activity.Add(new ActivityEntry(EntityType, clientId, "client.contactUpdated",
                    new { personId = contact.PersonId, person = contact.Person.FullName, fields = changed }, clientId));
                await _db.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return contact;
        }

        // Σβήνει μόνο τη σχέση: το πρόσωπο μένει (και στους άλλους πελάτες του)
        public async Task<bool> RemoveContactAsync(Guid clientId, Guid contactId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            var contact = await FindContactAsync(clientId, contactId, user, cancellationToken);
            if (contact == null) return false;

            _db.Remove(contact);
            _activity.Add(new ActivityEntry(EntityType, clientId, "client.contactRemoved",
                new { personId = contact.PersonId, person = contact.Person.FullName }, clientId));
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        private async Task<ClientContact?> FindContactAsync(Guid clientId, Guid contactId, ClaimsPrincipal user, CancellationToken cancellationToken)
        {
            var clients = Visible(user);
            return await _db.Set<ClientContact>().Include(cc => cc.Person)
                .FirstOrDefaultAsync(cc => cc.Id == contactId && cc.ClientId == clientId && clients.Any(c => c.Id == clientId), cancellationToken);
        }

        // Το πολύ μία κύρια επαφή (unique index): η παλιά παύει να είναι κύρια πριν σωθεί η νέα
        private async Task ClearPrimaryAsync(Guid clientId, Guid? exceptId, CancellationToken cancellationToken)
        {
            var current = await _db.Set<ClientContact>().Where(cc => cc.ClientId == clientId && cc.IsPrimary && cc.Id != exceptId).ToListAsync(cancellationToken);
            if (current.Count == 0) return;
            current.ForEach(cc => cc.IsPrimary = false);
            await _db.SaveChangesAsync(cancellationToken);
        }

        // --- Αρχεία (καρτέλα Files) ---

        // Ο πελάτης δεν ανήκει σε project: η σύνδεση δεν στενεύει την πρόσβαση (τα αρχεία projects θα έχουν το ProjectId τους)
        private static AttachmentTarget FileTarget(Guid clientId) => new(EntityType, clientId.ToString());

        public async Task<List<RecordFileDto>?> FilesAsync(Guid clientId, ClaimsPrincipal user, RecordFiles files, CancellationToken cancellationToken = default)
        {
            if (!await Visible(user).AnyAsync(c => c.Id == clientId, cancellationToken)) return null;
            var target = FileTarget(clientId);
            return await files.ListAsync([(target.EntityType, target.EntityId)], user, cancellationToken);
        }

        // false = ο πελάτης ή το αρχείο δεν υπάρχει / δεν το βλέπει ο χρήστης. Σύνδεση και ιστορικό στο ίδιο transaction.
        public async Task<bool> AttachFileAsync(Guid clientId, Guid mediaItemId, ClaimsPrincipal user, RecordFiles files, CancellationToken cancellationToken = default)
        {
            if (!await Visible(user).AnyAsync(c => c.Id == clientId, cancellationToken)) return false;

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            var attached = await files.AttachAsync(mediaItemId, FileTarget(clientId), user, cancellationToken);
            if (attached == null) return false;
            if (attached.Value.Created)
            {
                _activity.Add(new ActivityEntry(EntityType, clientId, "client.fileAttached", new { fileId = mediaItemId, file = attached.Value.Title }, clientId));
                await _db.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        // Σβήνει μόνο τη σύνδεση: το αρχείο μένει στη βιβλιοθήκη (DOC-01)
        public async Task<bool> DetachFileAsync(Guid clientId, Guid mediaItemId, ClaimsPrincipal user, RecordFiles files, CancellationToken cancellationToken = default)
        {
            if (!await Visible(user).AnyAsync(c => c.Id == clientId, cancellationToken)) return false;

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            var target = FileTarget(clientId);
            var title = await files.DetachAsync(mediaItemId, target.EntityType, target.EntityId, cancellationToken);
            if (title == null) return false;
            _activity.Add(new ActivityEntry(EntityType, clientId, "client.fileUnlinked", new { fileId = mediaItemId, file = title }, clientId));
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        // --- Βοηθητικά ---

        private static void Apply(Client client, ClientRequest request)
        {
            client.Kind = request.Kind!.Value;
            if (client.Kind == ClientKind.Individual)
            {
                client.FirstName = Clean(request.FirstName) ?? throw Invalid(nameof(ClientRequest.FirstName), "Enter the first name.");
                client.LastName = Clean(request.LastName) ?? throw Invalid(nameof(ClientRequest.LastName), "Enter the last name.");
                client.DisplayName = FullName(client.FirstName, client.LastName);
            }
            else
            {
                client.FirstName = null;
                client.LastName = null;
                client.DisplayName = Clean(request.DisplayName) ?? throw Invalid(nameof(ClientRequest.DisplayName), "Enter the client's name.");
            }
            client.LegalName = Clean(request.LegalName);
            client.TaxId = Clean(request.TaxId);
            client.TaxIdNormalized = CrmMatching.TaxId(request.TaxId);
            client.TaxOffice = Clean(request.TaxOffice);
            client.Status = request.Status;
            client.DefaultCurrency = Currency(request.DefaultCurrency);
            client.Tags = Tags(request.Tags);
            client.Addresses = request.Addresses
                .Where(a => !string.IsNullOrWhiteSpace(a.Line1))
                .Select(a => new PostalAddress
                {
                    Kind = a.Kind!.Value,
                    Line1 = a.Line1.Trim(),
                    Line2 = Clean(a.Line2),
                    City = Clean(a.City),
                    PostalCode = Clean(a.PostalCode),
                    Region = Clean(a.Region),
                    Country = Clean(a.Country),
                })
                // Η διεύθυνση τιμολόγησης πρώτη
                .OrderBy(a => a.Kind == AddressKind.Billing ? 0 : 1)
                .ToList();
            client.ContactMethods = ToContactMethods(request.ContactMethods, validate: true);
            client.MatchEmails = CrmMatching.Emails(client.ContactMethods);
            client.MatchPhones = CrmMatching.Phones(client.ContactMethods);
            client.Notes = Clean(request.Notes);

            // Ένας πελάτης χωρίς τρόπο επικοινωνίας δεν βοηθά κανέναν
            if (!client.ContactMethods.Any(m => m.Kind is ContactMethodKind.Email or ContactMethodKind.Phone or ContactMethodKind.Mobile))
                throw Invalid(nameof(ClientRequest.ContactMethods), "Add at least one email or phone number.");
        }

        internal static List<ContactMethod> ToContactMethods(IEnumerable<ContactMethodRequest> requests, bool validate)
        {
            var methods = requests
                .Where(m => m.Kind != null && !string.IsNullOrWhiteSpace(m.Value))
                .Select(m => new ContactMethod { Kind = m.Kind!.Value, Value = m.Value.Trim(), Label = Clean(m.Label) })
                .ToList();
            if (!validate) return methods;
            // Email έγκυρο· τηλέφωνα έγκυρα για τη χώρα τους και αποθηκευμένα σε E.164
            return methods.Select(m => m.Kind switch
            {
                ContactMethodKind.Email when !new EmailAddressAttribute().IsValid(m.Value) =>
                    throw Invalid(nameof(ClientRequest.ContactMethods), "\"{0}\" isn't a valid email address.", m.Value),
                ContactMethodKind.Phone or ContactMethodKind.Mobile => m with
                {
                    Value = Phones.ToE164(m.Value)
                        ?? throw Invalid(nameof(ClientRequest.ContactMethods), "\"{0}\" isn't a valid phone number. Check the country code and the number.", m.Value),
                },
                _ => m,
            }).ToList();
        }

        [GeneratedRegex("^[A-Z]{3}$")]
        private static partial Regex CurrencyCode();

        private static string Currency(string? value)
        {
            var code = (value ?? "").Trim().ToUpperInvariant();
            return CurrencyCode().IsMatch(code) ? code : throw Invalid(nameof(ClientRequest.DefaultCurrency), "Use a three-letter currency code, such as EUR.");
        }

        // Χωρίς κενά και διπλά (χωρίς διάκριση πεζών/κεφαλαίων), το πολύ 40 χαρακτήρες το καθένα
        private static List<string> Tags(IEnumerable<string> tags)
        {
            var clean = tags.Select(t => t?.Trim() ?? "").Where(t => t.Length > 0).DistinctBy(t => t.ToLowerInvariant()).ToList();
            return clean.Any(t => t.Length > 40) ? throw Invalid(nameof(ClientRequest.Tags), "Tags can be at most 40 characters long.") : clean;
        }

        internal static BusinessRuleException Invalid(string field, string message, params object[] arguments) =>
            new("validation", message, arguments) { StatusCode = StatusCodes.Status400BadRequest, Field = field };

        internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        internal static string FullName(string firstName, string? lastName) =>
            string.IsNullOrWhiteSpace(lastName) ? firstName : $"{firstName} {lastName}";

        // Ο χρήστης έστειλε το version που διάβασε: αν η εγγραφή άλλαξε στο μεταξύ, το Save δίνει 409
        internal static void ExpectVersion<T>(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T> entry, uint? version) where T : class
        {
            if (version is uint expected) entry.Property("RowVersion").OriginalValue = expected;
        }

        // Τα πεδία που άλλαξαν (camelCase, για το κείμενο του ιστορικού), χωρίς τα παραγόμενα
        internal List<string> ChangedFields<T>(T entity) where T : class
        {
            _db.ChangeTracker.DetectChanges();
            return _db.Entry(entity).Properties
                .Where(p => p.IsModified && !DerivedFields.Contains(p.Metadata.Name))
                .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Metadata.Name))
                .ToList();
        }
    }
}
