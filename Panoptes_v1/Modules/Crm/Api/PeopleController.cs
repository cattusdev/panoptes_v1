using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Panoptes_v1.Api;
using Panoptes_v1.Constants;
using Panoptes_v1.Modules.Crm.Services;

namespace Panoptes_v1.Modules.Crm.Api
{
    // Οι επαφές (πρόσωπα) και σε ποιους πελάτες ανήκουν. Νέα πρόσωπα και αλλαγές: Clients.Edit.
    [ApiController]
    [Route("api/crm/people")]
    [Authorize(Policy = Permissions.Clients.View)]
    public class PeopleController : ControllerBase
    {
        private readonly PersonService _people;

        public PeopleController(PersonService people)
        {
            _people = people;
        }

        // GET /api/crm/people?search=&clientId=&sort=&page=&pageSize=
        [HttpGet]
        public Task<PagedResult<PersonListItemDto>> List([FromQuery] PersonListRequest request, CancellationToken cancellationToken) =>
            _people.ListAsync(request, User, cancellationToken);

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<PersonDto>> Get(Guid id, CancellationToken cancellationToken)
        {
            var person = await _people.GetAsync(id, User, Permissions.Has(User, Permissions.Clients.Edit), cancellationToken);
            return person == null ? NotFound() : person;
        }

        [HttpPost]
        [Authorize(Policy = Permissions.Clients.Edit)]
        public async Task<ActionResult<PersonDto>> Create(PersonRequest request, CancellationToken cancellationToken)
        {
            var person = await _people.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = person.Id }, (await Get(person.Id, cancellationToken)).Value);
        }

        [HttpPut("{id:guid}")]
        [Authorize(Policy = Permissions.Clients.Edit)]
        public async Task<ActionResult<PersonDto>> Update(Guid id, PersonRequest request, CancellationToken cancellationToken)
        {
            var person = await _people.UpdateAsync(id, request, User, cancellationToken);
            return person == null ? NotFound() : await Get(id, cancellationToken);
        }

        // Πρόσωπα που ίσως υπάρχουν ήδη (πριν από τη δημιουργία νέου)
        [HttpPost("duplicates")]
        public async Task<IReadOnlyList<PersonMatchDto>> Duplicates(PersonRequest request, [FromQuery] Guid? excludeId, CancellationToken cancellationToken) =>
            await _people.FindDuplicatesAsync(request, excludeId, User, cancellationToken);
    }
}
