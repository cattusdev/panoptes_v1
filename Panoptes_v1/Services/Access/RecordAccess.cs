using System.Security.Claims;

namespace Panoptes_v1.Services.Access
{
    // Ποιες εγγραφές ενός τύπου βλέπει ένας χρήστης (ARCHITECTURE §3.1), ως IQueryable: κάθε λίστα, λεπτομέρεια, αναζήτηση,
    // export και widget ξεκινά από εδώ, ποτέ από το DbSet. Κάθε module υλοποιεί ένα για κάθε κύρια οντότητά του.
    public interface IRecordAccess<T> where T : class
    {
        IQueryable<T> Visible(ClaimsPrincipal user);
    }

    // Οντότητα χωρίς δικό της IRecordAccess: τη βλέπει όποιος βλέπει την εγγραφή στην οποία ανήκει
    // (π.χ. η σύνδεση πελάτη-επαφής ανήκει στον πελάτη). Χωρίς Owner: την εγγραφή που ορίζουν τα EntityType/EntityId
    // (ιστορικό, σημειώσεις, σχόλια), που την ελέγχει το module της εγγραφής. Το ελέγχει architecture test.
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class AccessThroughAttribute(Type? owner = null) : Attribute
    {
        public Type? Owner { get; } = owner;
    }
}
