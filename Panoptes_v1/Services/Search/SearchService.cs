using System.Security.Claims;

namespace Panoptes_v1.Services.Search
{
    // Ένα αποτέλεσμα της αναζήτησης: Type = ποια σελίδα το ανοίγει στον client ("client", "person", αργότερα "project"...)
    public record SearchHit(string Type, Guid Id, string Title, string? Subtitle, string? Reference, bool IsArchived);

    public record SearchGroup(string Type, IReadOnlyList<SearchHit> Hits);

    // Κάθε module δηλώνει τι ψάχνεται σε αυτό (ARCHITECTURE §10), πάντα πάνω στο δικό του access-filtered query.
    // Καταχώρηση: services.AddScoped<ISearchProvider, XSearch>().
    public interface ISearchProvider
    {
        Task<IReadOnlyList<SearchGroup>> SearchAsync(string term, ClaimsPrincipal user, int limit, CancellationToken cancellationToken);
    }

    // Η global αναζήτηση (command palette): όλα τα modules, έως `limit` αποτελέσματα ανά ομάδα. Ένα-ένα (ίδιο DbContext).
    public class SearchService
    {
        public const int MinLength = 2;
        public const int DefaultLimit = 5;

        private readonly IEnumerable<ISearchProvider> _providers;

        public SearchService(IEnumerable<ISearchProvider> providers)
        {
            _providers = providers;
        }

        public async Task<IReadOnlyList<SearchGroup>> SearchAsync(string? query, ClaimsPrincipal user, int limit = DefaultLimit, CancellationToken cancellationToken = default)
        {
            var term = query?.Trim() ?? "";
            if (term.Length < MinLength) return [];

            var groups = new List<SearchGroup>();
            foreach (var provider in _providers)
                groups.AddRange((await provider.SearchAsync(term, user, limit, cancellationToken)).Where(g => g.Hits.Count > 0));
            return groups;
        }
    }
}
