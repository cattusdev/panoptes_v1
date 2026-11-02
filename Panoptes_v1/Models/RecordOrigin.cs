namespace Panoptes_v1.Models
{
    // Από πού ήρθε μια business εγγραφή (ARCHITECTURE §2). Οι Historical/Import δεν προκαλούν ποτέ παρενέργειες
    // (emails, χρεώσεις, υπενθυμίσεις, αυτοματισμούς): μόνο το ιστορικό δραστηριότητας τις καταγράφει.
    public enum RecordOrigin
    {
        Live,
        Historical,
        Import,
    }
}
