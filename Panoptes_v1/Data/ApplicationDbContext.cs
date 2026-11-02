using Panoptes_v1.Models;
using Panoptes_v1.Models.Media;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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
        public DbSet<MediaAttachment> MediaAttachments { get; set; }
        public DbSet<BackgroundJob> BackgroundJobs { get; set; }
        public DbSet<Sequence> Sequences { get; set; }

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
                setting.Property(s => s.CreatedById).HasMaxLength(450);
                setting.Property(s => s.UpdatedById).HasMaxLength(450);
            });

            // Οι αριθμοί των κωδικών (CL-0042, CHG-2026-0153): μία γραμμή ανά σειρά, βλ. ReferenceGenerator
            builder.Entity<Sequence>(sequence =>
            {
                sequence.HasKey(s => s.Name);
                sequence.Property(s => s.Name).HasMaxLength(50);
            });

            builder.Entity<MediaUpload>(upload =>
            {
                upload.Property(u => u.FileName).HasMaxLength(255);
                upload.HasIndex(u => u.CreatedAt);
            });

            // Ένα αρχείο, πολλές εγγραφές. Restrict: αρχείο με συνδέσεις δεν σβήνεται (πρώτα αποσυνδέεται)
            builder.Entity<MediaAttachment>(attachment =>
            {
                attachment.Property(a => a.EntityType).HasMaxLength(50);
                attachment.Property(a => a.EntityId).HasMaxLength(64);
                attachment.Property(a => a.RequiredPermission).HasMaxLength(100);
                attachment.HasOne(a => a.MediaItem).WithMany().HasForeignKey(a => a.MediaItemId).OnDelete(DeleteBehavior.Restrict);
                attachment.HasIndex(a => new { a.MediaItemId, a.EntityType, a.EntityId }).IsUnique();
                attachment.HasIndex(a => new { a.EntityType, a.EntityId });
                attachment.HasIndex(a => a.ProjectId);
            });

            builder.Entity<BackgroundJob>(job =>
            {
                job.Property(j => j.Type).HasMaxLength(100);
                job.Property(j => j.IdempotencyKey).HasMaxLength(200);
                job.Property(j => j.Payload).HasColumnType("jsonb");
                job.Property(j => j.Status).HasConversion<string>().HasMaxLength(20);
                job.HasIndex(j => j.IdempotencyKey).IsUnique();
                job.HasIndex(j => new { j.Status, j.RunAt });
            });

            builder.Entity<AuditLog>(log =>
            {
                log.Property(l => l.Source).HasMaxLength(100);
                log.Property(l => l.TraceId).HasMaxLength(64);
                log.HasIndex(l => l.DateTime);
                log.HasIndex(l => new { l.TableName, l.PrimaryKey });
                log.HasIndex(l => l.UserId);
            });

            // Optimistic concurrency: το xmin της Postgres αλλάζει σε κάθε UPDATE. Αν η εγγραφή άλλαξε στο μεταξύ,
            // το SaveChanges πετάει DbUpdateConcurrencyException (-> 409 από τον GlobalExceptionHandler).
            builder.Entity<MediaFolder>().Property(f => f.RowVersion).IsRowVersion();
            builder.Entity<MediaItem>().Property(i => i.RowVersion).IsRowVersion();
            builder.Entity<UserProfile>().Property(p => p.RowVersion).IsRowVersion();
            builder.Entity<BrandingSettings>().Property(b => b.RowVersion).IsRowVersion();
            builder.Entity<AppSetting>().Property(s => s.RowVersion).IsRowVersion();
        }

        // Ό,τι αποθηκεύεται (sync ή async) περνάει από το audit. Τα SaveChanges()/SaveChangesAsync(ct) καταλήγουν εδώ.
        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            var pending = OnBeforeSaveChanges();
            if (pending.Count == 0)
                return base.SaveChanges(acceptAllChangesOnSuccess);

            // Οι εγγραφές με IDs από τη βάση γράφουν το log τους σε δεύτερο βήμα: και τα δύο μαζί ή τίποτα
            using var transaction = Database.CurrentTransaction == null ? Database.BeginTransaction() : null;
            var result = base.SaveChanges(acceptAllChangesOnSuccess);
            AuditLogs.AddRange(pending.Select(e => e.ToAuditLog()));
            base.SaveChanges(acceptAllChangesOnSuccess);
            transaction?.Commit();
            return result;
        }

        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            var pending = OnBeforeSaveChanges();
            if (pending.Count == 0)
                return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

            await using var transaction = Database.CurrentTransaction == null ? await Database.BeginTransactionAsync(cancellationToken) : null;
            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            AuditLogs.AddRange(pending.Select(e => e.ToAuditLog()));
            await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            if (transaction != null) await transaction.CommitAsync(cancellationToken);
            return result;
        }

        // Στήλες που δεν γράφονται ποτέ στο log, ανά πίνακα (όχι ανά όνομα: ένα PhoneNumber πελάτη πρέπει να καταγράφεται).
        // Επιπλέον: τα concurrency tokens (RowVersion, ConcurrencyStamp) και ό,τι έχει [AuditIgnore].
        private static readonly Dictionary<Type, HashSet<string>> SensitiveColumns = new()
        {
            [typeof(IdentityUser)] = ["PasswordHash", "SecurityStamp", "PhoneNumber"], // PhoneNumber: τηλέφωνο λογαριασμού (2FA/GDPR)
            [typeof(IdentityUserToken<string>)] = ["Value"], // authenticator key / recovery codes
            [typeof(AppSetting)] = ["Secret"],                // κρυπτογραφημένα μυστικά (π.χ. SMTP password)
        };

        // Τα IHasTimestamps τα έχει ήδη κάθε γραμμή του log (DateTime, UserId): δεν τα γράφουμε ξανά ως αλλαγές
        private static readonly HashSet<string> TimestampColumns =
            [nameof(IHasTimestamps.CreatedAt), nameof(IHasTimestamps.CreatedById), nameof(IHasTimestamps.UpdatedAt), nameof(IHasTimestamps.UpdatedById)];

        private static bool IsSensitive(PropertyEntry property, Type entityType)
        {
            if (property.Metadata.IsConcurrencyToken)
                return true;
            if (typeof(IHasTimestamps).IsAssignableFrom(entityType) && TimestampColumns.Contains(property.Metadata.Name))
                return true;
            if (property.Metadata.PropertyInfo?.IsDefined(typeof(AuditIgnoreAttribute), inherit: true) == true)
                return true;
            return SensitiveColumns.Any(s => s.Key.IsAssignableFrom(entityType) && s.Value.Contains(property.Metadata.Name));
        }

        // Created*/Updated* των IHasTimestamps: ο κώδικας δεν τα ορίζει ποτέ, και τα Created* δεν αλλάζουν μετά τη δημιουργία
        private void StampTimestamps(string? userId)
        {
            var now = DateTime.UtcNow;
            foreach (var entry in ChangeTracker.Entries<IHasTimestamps>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedById = userId;
                }
                else if (entry.State == EntityState.Modified)
                {
                    entry.Property(nameof(IHasTimestamps.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IHasTimestamps.CreatedById)).IsModified = false;
                }
                else
                {
                    continue;
                }
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedById = userId;
            }
        }

        // Γράφει τα logs που είναι έτοιμα· επιστρέφει όσα περιμένουν IDs από τη βάση
        private List<AuditEntry> OnBeforeSaveChanges()
        {
            ChangeTracker.DetectChanges();
            var auditEntries = new List<AuditEntry>();

            // Ποιος κάνει την κίνηση: το κομμάτι του συστήματος που το δήλωσε (job, CLI), αλλιώς ο χρήστης του request,
            // αλλιώς "system" (κώδικας χωρίς request που δεν δήλωσε τι είναι)
            var httpContext = _httpContextAccessor.HttpContext;
            var source = AuditContext.Source;
            var userId = source == null ? httpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier) : null;
            if (source == null && userId == null && httpContext == null) source = AuditContext.DefaultSource;
            var traceId = httpContext?.TraceIdentifier ?? Activity.Current?.TraceId.ToString();
            StampTimestamps(userId);

            foreach (var entry in ChangeTracker.Entries())
            {
                // Αγνοούμε τα ίδια τα AuditLogs (για να μην κάνουμε ατέρμονο βρόχο) και όσα δεν άλλαξαν
                if (entry.Entity is AuditLog || entry.State == EntityState.Detached || entry.State == EntityState.Unchanged)
                    continue;

                // Owned (π.χ. τα variants σε jsonb) και τα "θορυβώδη" (upload sessions, jobs) δεν χρειάζονται δικό τους log
                if (entry.Metadata.IsOwned() || entry.Entity is INotAudited)
                    continue;

                var auditEntry = new AuditEntry(entry)
                {
                    TableName = entry.Metadata.GetTableName() ?? entry.Entity.GetType().Name,
                    UserId = userId,
                    Source = source,
                    TraceId = traceId,
                    AuditType = entry.State switch
                    {
                        EntityState.Added => AuditType.Create,
                        EntityState.Deleted => AuditType.Delete,
                        _ => AuditType.Update,
                    },
                };

                foreach (var property in entry.Properties)
                {
                    if (IsSensitive(property, entry.Entity.GetType()))
                        continue;

                    string propertyName = property.Metadata.Name;

                    if (property.IsTemporary)
                    {
                        // Το ID (ή άλλη τιμή) δεν έχει δημιουργηθεί ακόμα: το διαβάζουμε μετά το Save
                        auditEntry.TemporaryProperties.Add(property);
                        continue;
                    }

                    if (property.Metadata.IsPrimaryKey())
                    {
                        auditEntry.KeyValues[propertyName] = property.CurrentValue;
                    }

                    switch (entry.State)
                    {
                        case EntityState.Added:
                            auditEntry.NewValues[propertyName] = property.CurrentValue;
                            break;
                        case EntityState.Deleted:
                            auditEntry.OldValues[propertyName] = property.OriginalValue;
                            break;
                        case EntityState.Modified:
                            if (property.IsModified)
                            {
                                auditEntry.ChangedColumns.Add(propertyName);
                                auditEntry.OldValues[propertyName] = property.OriginalValue;
                                auditEntry.NewValues[propertyName] = property.CurrentValue;
                            }
                            break;
                    }
                }

                auditEntries.Add(auditEntry);
            }

            // Όσα Logs είναι έτοιμα (δεν περιμένουν ID), τα βάζουμε να σωθούν μαζί με τις αλλαγές
            AuditLogs.AddRange(auditEntries.Where(e => !e.HasTemporaryProperties).Select(e => e.ToAuditLog()));

            // Επιστρέφουμε όσα Logs περιμένουν να παραχθεί ID από τη Βάση
            return auditEntries.Where(e => e.HasTemporaryProperties).ToList();
        }
    }
}