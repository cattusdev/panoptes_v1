using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Panoptes_v1.Api;
using Panoptes_v1.Constants;
using Panoptes_v1.Modules.Collaboration.Services;
using Panoptes_v1.Modules.Crm.Entities;
using Panoptes_v1.Modules.Crm.Services;
using Panoptes_v1.Services.Media;

namespace Panoptes_v1.Modules.Crm.Api
{
    // Οι πελάτες (/clients και Client 360°). Not found και no access δίνουν το ίδιο 404 (χωρίς διαρροή).
    [ApiController]
    [Route("api/crm/clients")]
    [Authorize(Policy = Permissions.Clients.View)]
    public class ClientsController : ControllerBase
    {
        private readonly ClientService _clients;
        private readonly PersonService _people;
        private readonly IActivityLog _activity;
        private readonly RecordFiles _files;

        public ClientsController(ClientService clients, PersonService people, IActivityLog activity, RecordFiles files)
        {
            _clients = clients;
            _people = people;
            _activity = activity;
            _files = files;
        }

        // Ταξινόμηση: name (προεπιλογή), ref, created
        private static readonly SortMap<Client> ClientSort = new SortMap<Client>("name", c => c.Id)
            .Add("name", c => c.DisplayName)
            .Add("ref", c => c.Ref)
            .Add("created", c => c.CreatedAt);

        // GET /api/crm/clients?search=&status=&kind=&tag=&sort=&page=&pageSize=
        [HttpGet]
        public async Task<PagedResult<ClientListItemDto>> List([FromQuery] ClientListRequest request, CancellationToken cancellationToken)
        {
            var query = _clients.Visible(User).AsNoTracking();
            query = request.Status switch
            {
                ClientListStatus.Active => query.Where(c => c.ArchivedAt == null && c.Status == ClientStatus.Active),
                ClientListStatus.Inactive => query.Where(c => c.ArchivedAt == null && c.Status == ClientStatus.Inactive),
                ClientListStatus.Archived => query.Where(c => c.ArchivedAt != null),
                ClientListStatus.All => query,
                _ => query.Where(c => c.ArchivedAt == null),
            };
            if (request.Kind is ClientKind kind) query = query.Where(c => c.Kind == kind);
            if (!string.IsNullOrWhiteSpace(request.Tag))
            {
                var tag = request.Tag.Trim();
                query = query.Where(c => c.Tags.Contains(tag));
            }
            if (request.SearchTerm is string term)
            {
                var pattern = Like.Contains(term);
                var taxId = CrmMatching.TaxId(term);
                query = query.Where(c =>
                    EF.Functions.ILike(c.DisplayName, pattern, Like.EscapeCharacter) ||
                    EF.Functions.ILike(c.LegalName ?? "", pattern, Like.EscapeCharacter) ||
                    EF.Functions.ILike(c.Ref, pattern, Like.EscapeCharacter) ||
                    (taxId != null && c.TaxIdNormalized == taxId));
            }

            var page = await ClientSort.Apply(query, request.Sort)
                .ToPagedAsync(request, c => new
                {
                    c.Id, c.Ref, c.Kind, c.DisplayName, c.LegalName, c.TaxId, c.Status, c.ArchivedAt, c.Tags, c.CreatedAt,
                    Primary = c.Contacts.Where(cc => cc.IsPrimary)
                        .Select(cc => new { cc.PersonId, cc.Person.FirstName, cc.Person.LastName, cc.Role, cc.Person.ContactMethods })
                        .FirstOrDefault(),
                }, cancellationToken);

            return page.Map(c => new ClientListItemDto(c.Id, c.Ref, c.Kind, c.DisplayName, c.LegalName, c.TaxId, c.Status, c.ArchivedAt != null, c.Tags,
                c.Primary == null ? null : new PrimaryContactDto(c.Primary.PersonId, ClientService.FullName(c.Primary.FirstName, c.Primary.LastName), c.Primary.Role,
                    c.Primary.ContactMethods.First(ContactMethodKind.Email), c.Primary.ContactMethods.First(ContactMethodKind.Mobile, ContactMethodKind.Phone)),
                c.CreatedAt.Utc()));
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<ClientDto>> Get(Guid id, CancellationToken cancellationToken)
        {
            var client = await _clients.Visible(User).AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
            return client == null ? NotFound() : ToDto(client);
        }

        [HttpPost]
        [Authorize(Policy = Permissions.Clients.Create)]
        public async Task<ActionResult<ClientDto>> Create(ClientRequest request, CancellationToken cancellationToken)
        {
            var client = await _clients.CreateAsync(request, User, cancellationToken: cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = client.Id }, ToDto(client));
        }

        [HttpPut("{id:guid}")]
        [Authorize(Policy = Permissions.Clients.Edit)]
        public async Task<ActionResult<ClientDto>> Update(Guid id, ClientRequest request, CancellationToken cancellationToken)
        {
            var client = await _clients.UpdateAsync(id, request, User, cancellationToken);
            return client == null ? NotFound() : ToDto(client);
        }

        [HttpPost("{id:guid}/archive")]
        [Authorize(Policy = Permissions.Clients.Archive)]
        public async Task<ActionResult<ClientDto>> Archive(Guid id, VersionRequest request, CancellationToken cancellationToken)
        {
            var client = await _clients.ArchiveAsync(id, request.Version, User, cancellationToken);
            return client == null ? NotFound() : ToDto(client);
        }

        [HttpPost("{id:guid}/unarchive")]
        [Authorize(Policy = Permissions.Clients.Archive)]
        public async Task<ActionResult<ClientDto>> Unarchive(Guid id, VersionRequest request, CancellationToken cancellationToken)
        {
            var client = await _clients.UnarchiveAsync(id, request.Version, User, cancellationToken);
            return client == null ? NotFound() : ToDto(client);
        }

        // Πιθανά διπλότυπα καθώς ο χρήστης συμπληρώνει τη φόρμα (το Save ελέγχει ξανά τον ΑΦΜ)
        [HttpPost("duplicates")]
        public async Task<DuplicateCheckDto> Duplicates(ClientDuplicateCheckRequest request, CancellationToken cancellationToken) =>
            new(await _clients.FindDuplicatesAsync(request, User, cancellationToken));

        // Οι ετικέτες που υπάρχουν ήδη (προτάσεις στη φόρμα και φίλτρο της λίστας)
        [HttpGet("tags")]
        public async Task<IReadOnlyList<string>> Tags(CancellationToken cancellationToken) =>
            await _clients.Visible(User).SelectMany(c => c.Tags).Distinct().OrderBy(t => t).Take(200).ToListAsync(cancellationToken);

        [HttpGet("{id:guid}/contacts")]
        public async Task<ActionResult<IReadOnlyList<ClientContactDto>>> Contacts(Guid id, CancellationToken cancellationToken)
        {
            var contacts = await _clients.ContactsAsync(id, User, cancellationToken);
            return contacts == null ? NotFound() : contacts;
        }

        [HttpPost("{id:guid}/contacts")]
        [Authorize(Policy = Permissions.Clients.Edit)]
        public async Task<ActionResult<IReadOnlyList<ClientContactDto>>> AddContact(Guid id, AddContactRequest request, CancellationToken cancellationToken)
        {
            var contact = await _clients.AddContactAsync(id, request, User, _people, cancellationToken);
            return contact == null ? NotFound() : await Contacts(id, cancellationToken);
        }

        [HttpPut("{id:guid}/contacts/{contactId:guid}")]
        [Authorize(Policy = Permissions.Clients.Edit)]
        public async Task<ActionResult<IReadOnlyList<ClientContactDto>>> UpdateContact(Guid id, Guid contactId, UpdateContactRequest request, CancellationToken cancellationToken)
        {
            var contact = await _clients.UpdateContactAsync(id, contactId, request, User, cancellationToken);
            return contact == null ? NotFound() : await Contacts(id, cancellationToken);
        }

        // Αφαιρεί μόνο τη σχέση: το πρόσωπο μένει στις Επαφές (και στους άλλους πελάτες του)
        [HttpDelete("{id:guid}/contacts/{contactId:guid}")]
        [Authorize(Policy = Permissions.Clients.Edit)]
        public async Task<ActionResult<IReadOnlyList<ClientContactDto>>> RemoveContact(Guid id, Guid contactId, CancellationToken cancellationToken) =>
            await _clients.RemoveContactAsync(id, contactId, User, cancellationToken) ? await Contacts(id, cancellationToken) : NotFound();

        // Τα αρχεία του πελάτη (χωρίς Media.View: κενή λίστα, το κατέβασμα ούτως ή άλλως το θέλει)
        [HttpGet("{id:guid}/files")]
        public async Task<ActionResult<IReadOnlyList<RecordFileDto>>> Files(Guid id, CancellationToken cancellationToken)
        {
            var files = await _clients.FilesAsync(id, User, _files, cancellationToken);
            return files == null ? NotFound() : files;
        }

        // Σύνδεση αρχείου της βιβλιοθήκης (ή αρχείου που μόλις ανέβηκε από την καρτέλα)
        [HttpPost("{id:guid}/files")]
        [Authorize(Policy = Permissions.Clients.Edit)]
        public async Task<ActionResult<IReadOnlyList<RecordFileDto>>> AttachFile(Guid id, AttachFileRequest request, CancellationToken cancellationToken) =>
            await _clients.AttachFileAsync(id, request.MediaItemId!.Value, User, _files, cancellationToken) ? await Files(id, cancellationToken) : NotFound();

        // Αποσύνδεση: μόνο η σύνδεση, το αρχείο μένει στη βιβλιοθήκη
        [HttpDelete("{id:guid}/files/{mediaItemId:guid}")]
        [Authorize(Policy = Permissions.Clients.Edit)]
        public async Task<ActionResult<IReadOnlyList<RecordFileDto>>> DetachFile(Guid id, Guid mediaItemId, CancellationToken cancellationToken) =>
            await _clients.DetachFileAsync(id, mediaItemId, User, _files, cancellationToken) ? await Files(id, cancellationToken) : NotFound();

        // Το ιστορικό του πελάτη, το πιο πρόσφατο πρώτο
        [HttpGet("{id:guid}/activity")]
        public async Task<ActionResult<PagedResult<ActivityItem>>> Activity(Guid id, [FromQuery] ActivityListRequest request, CancellationToken cancellationToken)
        {
            if (!await _clients.Visible(User).AnyAsync(c => c.Id == id, cancellationToken)) return NotFound();
            return await _activity.ForClientAsync(id, User, request, cancellationToken);
        }

        private ClientDto ToDto(Client client) => new(
            client.Id,
            client.Ref,
            client.Kind,
            client.DisplayName,
            client.FirstName,
            client.LastName,
            client.LegalName,
            client.TaxId,
            client.TaxOffice,
            client.Status,
            client.DefaultCurrency,
            client.Tags,
            client.Addresses.Select(a => a.ToDto()).ToList(),
            client.ContactMethods.Select(m => m.ToDto()).ToList(),
            client.Notes,
            client.Origin,
            client.ArchivedAt.Utc(),
            client.CreatedAt.Utc(),
            client.RowVersion,
            Permissions.Has(User, Permissions.Clients.Edit),
            Permissions.Has(User, Permissions.Clients.Archive));
    }
}
