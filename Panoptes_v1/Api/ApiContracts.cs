using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Panoptes_v1.Api
{
    // Κοινή μορφή για λίστες με σελίδες
    public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
    {
        public PagedResult<TOut> Map<TOut>(Func<T, TOut> map) => new(Items.Select(map).ToList(), Total, Page, PageSize);
    }

    public static class Paging
    {
        public const int MaxPageSize = 100;

        public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
            (Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize));
    }

    // Το query string κάθε λίστας: ?search=&sort=&page=&pageSize=. Τα φίλτρα μιας λίστας μπαίνουν σε υποκλάση με
    // typed ιδιότητες (enum, Guid, DateOnly...): μια άκυρη τιμή γίνεται 400 από το model binding, δεν αγνοείται σιωπηλά.
    //   public async Task<PagedResult<XDto>> List([FromQuery] XListRequest request) =>
    //       (await XSort.Apply(Filter(query, request), request.Sort).ToPagedAsync(request)).Map(ToDto);
    public class ListRequest
    {
        public string? Search { get; set; }
        public string? Sort { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;

        // Το κείμενο αναζήτησης χωρίς κενά γύρω, null αν δεν δόθηκε
        public string? SearchTerm => string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
    }

    public static class ListQueries
    {
        // Μία σελίδα από ήδη ταξινομημένο query (SortMap.Apply): πρώτα το σύνολο, μετά οι γραμμές της σελίδας
        public static Task<PagedResult<T>> ToPagedAsync<T>(this IOrderedQueryable<T> query, ListRequest request, CancellationToken cancellationToken = default) =>
            query.ToPagedAsync(request.Page, request.PageSize, cancellationToken);

        public static async Task<PagedResult<T>> ToPagedAsync<T>(this IOrderedQueryable<T> query, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            (page, pageSize) = Paging.Normalize(page, pageSize);
            var total = await query.CountAsync(cancellationToken);
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
            return new PagedResult<T>(items, total, page, pageSize);
        }
    }

    // Αναζήτηση με ILIKE: τα %, _ και \ του χρήστη ψάχνονται ως χαρακτήρες, όχι ως wildcards.
    //   query.Where(c => EF.Functions.ILike(c.Name, Like.Contains(term), Like.EscapeCharacter))
    // Το EscapeCharacter χρειάζεται πάντα: χωρίς αυτό το Npgsql γράφει ESCAPE '' και η αναζήτηση "50%" δεν βρίσκει τίποτα.
    public static class Like
    {
        public const string EscapeCharacter = "\\";

        public static string Escape(string value) =>
            value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

        public static string Contains(string term) => $"%{Escape(term)}%";

        public static string StartsWith(string term) => $"{Escape(term)}%";
    }

    // Ταξινόμηση λίστας από το query string: "email" = αύξουσα, "-email" = φθίνουσα.
    // Μόνο τα πεδία που δηλώνει το endpoint (whitelist)· άγνωστο ή κενό -> η προεπιλογή.
    // Το tieBreaker (μοναδικό πεδίο, π.χ. το Id) μπαίνει πάντα τελευταίο: σταθερή σειρά ανάμεσα στις σελίδες.
    public class SortMap<T>
    {
        private readonly Dictionary<string, Func<IQueryable<T>, bool, IOrderedQueryable<T>>> _fields = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _default;
        private readonly Func<IOrderedQueryable<T>, IOrderedQueryable<T>> _tieBreaker;

        public SortMap(string defaultSort, Expression<Func<T, object>> tieBreaker, bool tieBreakerDescending = false)
        {
            _default = defaultSort;
            _tieBreaker = tieBreakerDescending ? query => query.ThenByDescending(tieBreaker) : query => query.ThenBy(tieBreaker);
        }

        public SortMap<T> Add<TKey>(string name, Expression<Func<T, TKey>> key)
        {
            _fields[name] = (query, descending) => descending ? query.OrderByDescending(key) : query.OrderBy(key);
            return this;
        }

        public IOrderedQueryable<T> Apply(IQueryable<T> query, string? sort)
        {
            var (name, descending) = Parse(sort);
            if (!_fields.ContainsKey(name)) (name, descending) = Parse(_default);
            return _tieBreaker(_fields[name](query, descending));
        }

        private static (string Name, bool Descending) Parse(string? sort) =>
            string.IsNullOrWhiteSpace(sort) ? ("", false)
            : sort.StartsWith('-') ? (sort[1..], true)
            : (sort, false);
    }
}
