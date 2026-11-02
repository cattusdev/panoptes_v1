using Panoptes_v1.Models;
using Panoptes_v1.Models.Media;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Panoptes_v1.Data
{
    public class ApplicationDbContext : IdentityDbContext<IdentityUser>
    {
        // ΠΡΟΣΘΗΚΗ: Το εργαλείο για να διαβάζουμε τον τρέχοντα χρήστη
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            IHttpContextAccessor httpContextAccessor) // Κάνουμε Inject το Accessor
            : base(options)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<MediaFolder> MediaFolders { get; set; }
        public DbSet<MediaItem> MediaItems { get; set; }
        public DbSet<MediaVersion> MediaVersions { get; set; }
        public DbSet<MediaUpload> MediaUploads { get; set; }
        public DbSet<UserProfile> UserProfiles { get; set; }
        public DbSet<BrandingSettings> Branding { get; set; }
        public DbSet<AppSetting> AppSettings { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<MediaFolder>(folder =>
            {
                folder.Property(f => f.Name).HasMaxLength(100);
                folder.Property(f => f.NormalizedName).HasMaxLength(100);
                folder.HasOne(f => f.Parent).WithMany().HasForeignKey(f => f.ParentId).OnDelete(DeleteBehavior.Restrict);

                // Μοναδικό όνομα ανά γονέα. Στη ρίζα (ParentId NULL) χρειάζεται δικό του index, γιατί στο Postgres τα NULL διαφέρουν μεταξύ τους.
                folder.HasIndex(f => new { f.ParentId, f.NormalizedName }).IsUnique().HasFilter("\"ParentId\" IS NOT NULL");
                folder.HasIndex(f => f.NormalizedName).IsUnique().HasFilter("\"ParentId\" IS NULL");
            });

            builder.Entity<MediaItem>(item =>
            {
                item.Property(i => i.Sequence).UseIdentityByDefaultColumn();
                item.HasIndex(i => i.Sequence).IsUnique();
                item.Property(i => i.Title).HasMaxLength(255);
                item.Property(i => i.AltText).HasMaxLength(500);
                item.Property(i => i.Description).HasMaxLength(2000);
                item.HasOne(i => i.Folder).WithMany().HasForeignKey(i => i.FolderId).OnDelete(DeleteBehavior.SetNull);
                item.HasMany(i => i.Versions).WithOne(v => v.MediaItem).HasForeignKey(v => v.MediaItemId).OnDelete(DeleteBehavior.Cascade);
                item.HasIndex(i => new { i.Purpose, i.DeletedAt, i.FolderId, i.CreatedAt });
                item.HasIndex(i => i.OwnerId);
            });

            builder.Entity<MediaVersion>(version =>
            {
                version.Property(v => v.OriginalFileName).HasMaxLength(255);
                version.Property(v => v.Extension).HasMaxLength(10);
                version.Property(v => v.ContentType).HasMaxLength(100);
                version.Property(v => v.Sha256).HasMaxLength(64);
                version.Property(v => v.DominantColor).HasMaxLength(7);
                version.HasIndex(v => new { v.MediaItemId, v.Number }).IsUnique();
                version.OwnsMany(v => v.Variants, variant => variant.ToJson());
            });

            builder.Entity<UserProfile>(profile =>
            {
                profile.HasKey(p => p.UserId);
                profile.HasOne<IdentityUser>().WithOne().HasForeignKey<UserProfile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
                profile.HasOne(p => p.Avatar).WithMany().HasForeignKey(p => p.AvatarMediaId).OnDelete(DeleteBehavior.SetNull);
                profile.Property(p => p.Language).HasMaxLength(10);
            });

            builder.Entity<BrandingSettings>(branding =>
            {
                branding.ToTable("BrandingSettings");
                branding.Property(b => b.Id).ValueGeneratedNever();
                branding.Property(b => b.AppName).HasMaxLength(50);
                branding.HasOne(b => b.Logo).WithMany().HasForeignKey(b => b.LogoMediaId).OnDelete(DeleteBehavior.SetNull);
                branding.HasOne(b => b.Favicon).WithMany().HasForeignKey(b => b.FaviconMediaId).OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<AppSetting>(setting =>
            {
                setting.HasKey(s => s.Key);
                setting.Property(s => s.Key).HasMaxLength(100);
                setting.Property(s => s.Value).HasColumnType("jsonb");
            });

            builder.Entity<MediaUpload>(upload =>
            {
                upload.Property(u => u.FileName).HasMaxLength(255);
                upload.HasIndex(u => u.CreatedAt);
            });
        }

        // OVERRIDE: Πιάνουμε το SaveChanges πριν εκτελεστεί!
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var auditEntries = OnBeforeSaveChanges(); // Δες τι άλλαξε
            var result = await base.SaveChangesAsync(cancellationToken); // Σώσε τις πραγματικές αλλαγές
            await OnAfterSaveChangesAsync(auditEntries); // Σώσε και τα Logs!
            return result;
        }

        private List<AuditEntry> OnBeforeSaveChanges()
        {
            ChangeTracker.DetectChanges();
            var auditEntries = new List<AuditEntry>();

            // Βρες το ID του χρήστη που κάνει την κίνηση (αν είναι Logged in)
            var userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

            foreach (var entry in ChangeTracker.Entries())
            {
                // Αγνοούμε τα ίδια τα AuditLogs (για να μην κάνουμε ατέρμονο βρόχο) και όσα δεν άλλαξαν
                if (entry.Entity is AuditLog || entry.State == EntityState.Detached || entry.State == EntityState.Unchanged)
                    continue;

                // Owned (π.χ. τα variants σε jsonb) και τα "θορυβώδη" (upload sessions) δεν χρειάζονται δικό τους log
                if (entry.Metadata.IsOwned() || entry.Entity is INotAudited)
                    continue;

                var auditEntry = new AuditEntry(entry)
                {
                    TableName = entry.Metadata.GetTableName() ?? entry.Entity.GetType().Name,
                    UserId = userId
                };
                auditEntries.Add(auditEntry);

                foreach (var property in entry.Properties)
                {
                    if (property.IsTemporary)
                    {
                        // Αν το ID δεν έχει δημιουργηθεί ακόμα (π.χ. νέος χρήστης), το προσπερνάμε προς το παρόν
                        continue;
                    }

                    string propertyName = property.Metadata.Name;
                    
                    var sensitiveColumns = new List<string>
                    {
                        "PasswordHash",
                        "SecurityStamp",
                        "ConcurrencyStamp",
                        "PhoneNumber", // Αν θεωρείς τα τηλέφωνα GDPR sensitive
                        "Secret" // AppSettings: κρυπτογραφημένα μυστικά (π.χ. SMTP password)
                    };

                    if (sensitiveColumns.Contains(propertyName))
                    {
                        continue; // Αγνοούμε εντελώς αυτή τη στήλη!
                    }

                    // AspNetUserTokens.Value = authenticator key / recovery codes: ποτέ στο log
                    if (entry.Entity is IdentityUserToken<string> && propertyName == "Value")
                    {
                        continue;
                    }

                    if (property.Metadata.IsPrimaryKey())
                    {
                        auditEntry.KeyValues[propertyName] = property.CurrentValue;
                    }

                    switch (entry.State)
                    {
                        case EntityState.Added:
                            auditEntry.AuditType = AuditType.Create;
                            auditEntry.NewValues[propertyName] = property.CurrentValue;
                            break;
                        case EntityState.Deleted:
                            auditEntry.AuditType = AuditType.Delete;
                            auditEntry.OldValues[propertyName] = property.OriginalValue;
                            break;
                        case EntityState.Modified:
                            if (property.IsModified)
                            {
                                auditEntry.ChangedColumns.Add(propertyName);
                                auditEntry.AuditType = AuditType.Update;
                                auditEntry.OldValues[propertyName] = property.OriginalValue;
                                auditEntry.NewValues[propertyName] = property.CurrentValue;
                            }
                            break;
                    }
                }
            }

            // Όσα Logs είναι έτοιμα (δεν περιμένουν ID), τα βάζουμε να σωθούν
            foreach (var auditEntry in auditEntries.Where(_ => !_.Entry.Properties.Any(p => p.IsTemporary)))
            {
                AuditLogs.Add(auditEntry.ToAuditLog());
            }

            // Επιστρέφουμε όσα Logs περιμένουν να παραχθεί ID από τη Βάση
            return auditEntries.Where(_ => _.Entry.Properties.Any(p => p.IsTemporary)).ToList();
        }

        private Task OnAfterSaveChangesAsync(List<AuditEntry> auditEntries)
        {
            if (auditEntries == null || auditEntries.Count == 0)
                return Task.CompletedTask;

            // Τώρα που έγινε το Save, η βάση έφτιαξε τα νέα IDs. Τα παίρνουμε και τα βάζουμε στα Logs.
            foreach (var auditEntry in auditEntries)
            {
                foreach (var prop in auditEntry.Entry.Properties)
                {
                    if (prop.IsTemporary)
                    {
                        auditEntry.KeyValues[prop.Metadata.Name] = prop.CurrentValue;
                        if (auditEntry.AuditType == AuditType.Create)
                            auditEntry.NewValues[prop.Metadata.Name] = prop.CurrentValue;
                    }
                }
                AuditLogs.Add(auditEntry.ToAuditLog());
            }

            // Σώζουμε τα Logs των νέων εγγραφών
            return base.SaveChangesAsync();
        }
    }
}