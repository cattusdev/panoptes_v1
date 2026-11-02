using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Panoptes_v1.Services.Search;

namespace Panoptes_v1.Api
{
    public record SearchResultDto(string Query, IReadOnlyList<SearchGroup> Groups);

    // GET /api/search?q= : η αναζήτηση του command palette. Κάθε module φιλτράρει μόνο του ό,τι δεν βλέπει ο χρήστης.
    [ApiController]
    [Route("api/search")]
    [Authorize]
    public class SearchController : ControllerBase
    {
        private readonly SearchService _search;

        public SearchController(SearchService search)
        {
            _search = search;
        }

        [HttpGet]
        public async Task<SearchResultDto> Search([FromQuery] string? q, CancellationToken cancellationToken)
        {
            var query = q?.Trim() ?? "";
            if (query.Length > 100) query = query[..100];
            return new SearchResultDto(query, await _search.SearchAsync(query, User, cancellationToken: cancellationToken));
        }
    }
}
