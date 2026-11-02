namespace Panoptes_v1.Data
{
    // Συναρτήσεις του pg_trgm για LINQ (καταχωρούνται στο ApplicationDbContext). Μόνο μέσα σε queries της βάσης.
    //   query.Where(c => Trigram.Similarity(c.DisplayName, name) >= 0.6)
    public static class Trigram
    {
        // 0..1: πόσο μοιάζουν δύο κείμενα (χωρίς διάκριση πεζών/κεφαλαίων)
        public static double Similarity(string? a, string? b) =>
            throw new InvalidOperationException("Trigram.Similarity runs only inside database queries.");
    }
}
