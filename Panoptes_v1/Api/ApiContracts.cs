using System.Linq.Expressions;

namespace Panoptes_v1.Api
{
    // Κοινή μορφή για λίστες με σελίδες
    public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

    public static class Paging
    {
        public const int MaxPageSize = 100;

        public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
            (Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize));
    }

    // Ταξινόμηση λίστας από το query string: "email" = αύξουσα, "-email" = φθίνουσα.
    // Μόνο τα πεδία που δηλώνει το endpoint (whitelist)· άγνωστο ή κενό -> η προεπιλογή. Ο καλών προσθέτει ThenBy για σταθερή σειρά.
    public class SortMap<T>
    {
        private readonly Dictionary<string, Func<IQueryable<T>, bool, IOrderedQueryable<T>>> _fields = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _default;

        public SortMap(string defaultSort) => _default = defaultSort;

        public SortMap<T> Add<TKey>(string name, Expression<Func<T, TKey>> key)
        {
            _fields[name] = (query, descending) => descending ? query.OrderByDescending(key) : query.OrderBy(key);
            return this;
        }

        public IOrderedQueryable<T> Apply(IQueryable<T> query, string? sort)
        {
            var (name, descending) = Parse(sort);
            if (!_fields.ContainsKey(name)) (name, descending) = Parse(_default);
            return _fields[name](query, descending);
        }

        private static (string Name, bool Descending) Parse(string? sort) =>
            string.IsNullOrWhiteSpace(sort) ? ("", false)
            : sort.StartsWith('-') ? (sort[1..], true)
            : (sort, false);
    }
}
