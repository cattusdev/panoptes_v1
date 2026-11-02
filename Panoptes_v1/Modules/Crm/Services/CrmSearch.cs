using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Panoptes_v1.Api;
using Panoptes_v1.Data;
using Panoptes_v1.Modules.Crm.Entities;
using Panoptes_v1.Services.Access;
using Panoptes_v1.Services.Search;

namespace Panoptes_v1.Modules.Crm.Services
{
    // Πελάτες (όνομα, επωνυμία, κωδικός, ΑΦΜ) και πρόσωπα (όνομα, email, τηλέφωνο) για τη global αναζήτηση.
    // Οι αρχειοθετημένοι βρίσκονται κι αυτοί (με σήμανση), μετά τους ενεργούς.
    public class CrmSearch : ISearchProvider
    {
        private readonly IRecordAccess<Client> _clients;
        private readonly IRecordAccess<Person> _people;

        public CrmSearch(IRecordAccess<Client> clients, IRecordAccess<Person> people)
        {
            _clients = clients;
            _people = people;
        }

        public async Task<IReadOnlyList<SearchGroup>> SearchAsync(string term, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
        {
            var pattern = Like.Contains(term);
            var taxId = CrmMatching.TaxId(term);

            var clients = await _clients.Visible(user).AsNoTracking()
                .Where(c =>
                    EF.Functions.ILike(c.DisplayName, pattern, Like.EscapeCharacter) ||
                    EF.Functions.ILike(c.LegalName ?? "", pattern, Like.EscapeCharacter) ||
                    EF.Functions.ILike(c.Ref, pattern, Like.EscapeCharacter) ||
                    (taxId != null && c.TaxIdNormalized == taxId))
                .OrderBy(c => c.ArchivedAt != null)
                .ThenByDescending(c => Trigram.Similarity(c.DisplayName, term))
                .ThenBy(c => c.DisplayName).ThenBy(c => c.Id)
                .Take(limit)
                .Select(c => new SearchHit("client", c.Id, c.DisplayName, c.LegalName, c.Ref, c.ArchivedAt != null))
                .ToListAsync(cancellationToken);

            var email = CrmMatching.Email(term);
            var phone = CrmMatching.Phone(term);
            var people = await _people.Visible(user).AsNoTracking()
                .Where(p =>
                    EF.Functions.ILike(p.FirstName + " " + (p.LastName ?? ""), pattern, Like.EscapeCharacter) ||
                    p.MatchEmails.Contains(email!) ||
                    (phone != null && p.MatchPhones.Contains(phone)))
                .OrderByDescending(p => Trigram.Similarity(p.FirstName + " " + (p.LastName ?? ""), term))
                .ThenBy(p => p.FirstName).ThenBy(p => p.Id)
                .Take(limit)
                .Select(p => new
                {
                    p.Id, p.FirstName, p.LastName,
                    Client = p.Clients.OrderByDescending(cc => cc.IsPrimary).Select(cc => cc.Client.DisplayName).FirstOrDefault(),
                })
                .ToListAsync(cancellationToken);

            return
            [
                new SearchGroup("client", clients),
                new SearchGroup("person", people.Select(p => new SearchHit("person", p.Id, ClientService.FullName(p.FirstName, p.LastName), p.Client, null, false)).ToList()),
            ];
        }
    }
}
