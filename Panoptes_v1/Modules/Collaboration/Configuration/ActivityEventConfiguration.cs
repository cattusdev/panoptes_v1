using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Panoptes_v1.Modules.Collaboration.Entities;

namespace Panoptes_v1.Modules.Collaboration.Configuration
{
    public class ActivityEventConfiguration : IEntityTypeConfiguration<ActivityEvent>
    {
        public void Configure(EntityTypeBuilder<ActivityEvent> activity)
        {
            activity.ToTable("ActivityEvents");
            activity.Property(e => e.EntityType).HasMaxLength(50);
            activity.Property(e => e.Verb).HasMaxLength(100);
            activity.Property(e => e.Data).HasColumnType("jsonb");
            activity.Property(e => e.ActorId).HasMaxLength(450);
            activity.Property(e => e.Source).HasMaxLength(100);
            activity.HasIndex(e => new { e.EntityType, e.EntityId, e.OccurredAt });
            activity.HasIndex(e => new { e.ClientId, e.OccurredAt });
            activity.HasIndex(e => e.ProjectId);
        }
    }
}
