using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Panoptes_v1.Modules.Crm.Entities;

namespace Panoptes_v1.Modules.Crm.Configuration
{
    public class ClientConfiguration : IEntityTypeConfiguration<Client>
    {
        public void Configure(EntityTypeBuilder<Client> client)
        {
            client.ToTable("Clients");
            client.Property(c => c.Ref).HasMaxLength(20);
            client.HasIndex(c => c.Ref).IsUnique();
            client.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20);
            client.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            client.Property(c => c.Origin).HasConversion<string>().HasMaxLength(20);
            client.Property(c => c.DisplayName).HasMaxLength(200);
            client.Property(c => c.LegalName).HasMaxLength(300);
            client.Property(c => c.TaxId).HasMaxLength(30);
            client.Property(c => c.TaxIdNormalized).HasMaxLength(30);
            client.Property(c => c.TaxOffice).HasMaxLength(100);
            client.Property(c => c.DefaultCurrency).HasMaxLength(3).IsFixedLength();
            client.Property(c => c.Notes).HasMaxLength(4000);
            client.Property(c => c.SourceRef).HasMaxLength(200);
            client.Property(c => c.CreatedById).HasMaxLength(450);
            client.Property(c => c.UpdatedById).HasMaxLength(450);
            client.Property(c => c.ArchivedById).HasMaxLength(450);
            client.Property(c => c.Addresses).AsJsonList();
            client.Property(c => c.ContactMethods).AsJsonList();
            client.Property(c => c.RowVersion).IsRowVersion();

            // Αναζήτηση (ILIKE) και ομοιότητα ονομάτων (pg_trgm) για τα διπλότυπα
            client.HasIndex(c => c.DisplayName).HasMethod("gin").HasOperators("gin_trgm_ops");
            client.HasIndex(c => c.LegalName).HasMethod("gin").HasOperators("gin_trgm_ops");
            client.HasIndex(c => c.TaxIdNormalized);
            client.HasIndex(c => c.MatchEmails).HasMethod("gin");
            client.HasIndex(c => c.MatchPhones).HasMethod("gin");
            client.HasIndex(c => c.Tags).HasMethod("gin");
        }
    }

    public class PersonConfiguration : IEntityTypeConfiguration<Person>
    {
        public void Configure(EntityTypeBuilder<Person> person)
        {
            person.ToTable("People");
            person.Property(p => p.FirstName).HasMaxLength(100);
            person.Property(p => p.LastName).HasMaxLength(100);
            person.Property(p => p.Notes).HasMaxLength(4000);
            person.Property(p => p.CreatedById).HasMaxLength(450);
            person.Property(p => p.UpdatedById).HasMaxLength(450);
            person.Property(p => p.ContactMethods).AsJsonList();
            person.Property(p => p.RowVersion).IsRowVersion();

            person.HasIndex(p => p.FirstName).HasMethod("gin").HasOperators("gin_trgm_ops");
            person.HasIndex(p => p.LastName).HasMethod("gin").HasOperators("gin_trgm_ops");
            person.HasIndex(p => p.MatchEmails).HasMethod("gin");
            person.HasIndex(p => p.MatchPhones).HasMethod("gin");
        }
    }

    public class ClientContactConfiguration : IEntityTypeConfiguration<ClientContact>
    {
        public void Configure(EntityTypeBuilder<ClientContact> contact)
        {
            contact.ToTable("ClientContacts");
            contact.Property(c => c.Role).HasMaxLength(100);
            contact.Property(c => c.CreatedById).HasMaxLength(450);
            contact.Property(c => c.UpdatedById).HasMaxLength(450);
            contact.Property(c => c.RowVersion).IsRowVersion();
            contact.HasOne(c => c.Client).WithMany(c => c.Contacts).HasForeignKey(c => c.ClientId).OnDelete(DeleteBehavior.Restrict);
            contact.HasOne(c => c.Person).WithMany(p => p.Clients).HasForeignKey(c => c.PersonId).OnDelete(DeleteBehavior.Restrict);

            // Ένα πρόσωπο μία φορά ανά πελάτη, και το πολύ μία κύρια επαφή ανά πελάτη
            contact.HasIndex(c => new { c.ClientId, c.PersonId }).IsUnique();
            contact.HasIndex(c => c.ClientId).IsUnique().HasFilter("\"IsPrimary\"").HasDatabaseName("IX_ClientContacts_ClientId_Primary");
            contact.HasIndex(c => c.PersonId);
        }
    }

    // Μικρές λίστες (διευθύνσεις, τρόποι επικοινωνίας) ως jsonb σε μία στήλη: φορτώνονται πάντα μαζί με την εγγραφή
    // και το audit log τις γράφει ως μία τιμή (παλιά/νέα). Τα enums ως κείμενο, όπως στις στήλες.
    internal static class JsonListMapping
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

        public static PropertyBuilder<List<T>> AsJsonList<T>(this PropertyBuilder<List<T>> property) =>
            property
                .HasColumnType("jsonb")
                .HasConversion(
                    value => JsonSerializer.Serialize(value, Options),
                    json => JsonSerializer.Deserialize<List<T>>(json, Options) ?? new List<T>(),
                    new ValueComparer<List<T>>(
                        (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
                        value => JsonSerializer.Serialize(value, Options).GetHashCode(),
                        value => JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(value, Options), Options)!));
    }
}
