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
}
