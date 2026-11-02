namespace Panoptes_v1.Models
{
    // Πότε και από ποιον γράφτηκε / άλλαξε τελευταία μια εγγραφή. Τα συμπληρώνει το ApplicationDbContext στο Save,
    // ποτέ ο κώδικας με το χέρι. Είναι ο χρόνος πληκτρολόγησης, όχι επιχειρηματική ημερομηνία (αυτές είναι DateOnly).
    // Χρήστης null = δεν υπήρχε χρήστης (job, CLI): ποιος το έκανε το λέει το Source του audit log.
    public interface IHasTimestamps
    {
        DateTime CreatedAt { get; set; }
        string? CreatedById { get; set; }
        DateTime UpdatedAt { get; set; }
        string? UpdatedById { get; set; }
    }
}
