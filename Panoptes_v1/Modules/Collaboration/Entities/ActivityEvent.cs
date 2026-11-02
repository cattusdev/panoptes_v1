using Panoptes_v1.Services.Access;

namespace Panoptes_v1.Modules.Collaboration.Entities
{
    // Μια γραμμή του ιστορικού δραστηριότητας (ARCHITECTURE §4.5, §9.2): "η Μαρία αρχειοθέτησε τον πελάτη".
    // Γράφεται από τις services μέσα στο ίδιο transaction με την αλλαγή, και δεν αλλάζει ποτέ μετά.
    // ClientId: ο πελάτης της εγγραφής (αν έχει), για την καρτέλα Activity του Client 360°.
    // ProjectId: το project της εγγραφής· όποιος δεν βλέπει το project δεν βλέπει ούτε τη γραμμή.
    // Τη βλέπει όποιος βλέπει την εγγραφή (EntityType/EntityId): ο έλεγχος γίνεται από το module της εγγραφής.
    [AccessThrough]
    public class ActivityEvent
    {
        public Guid Id { get; set; }
        public string EntityType { get; set; } = "";
        public Guid EntityId { get; set; }
        public Guid? ClientId { get; set; }
        public Guid? ProjectId { get; set; }
        public string Verb { get; set; } = "";             // π.χ. "client.archived"
        public string Data { get; set; } = "{}";           // jsonb: οι τιμές για το κείμενο (ονόματα, αιτιολογία...)
        public string? ActorId { get; set; }               // ο χρήστης· null = job/CLI (βλ. Source)
        public string? Source { get; set; }
        public DateTime OccurredAt { get; set; }
    }
}
