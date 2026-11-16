using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Domain.Catalog;

namespace Shop.Infrastructure.Persistence.Configurations.Catalog;

internal sealed class SyncRunConfiguration : IEntityTypeConfiguration<SyncRun>
{
    public void Configure(EntityTypeBuilder<SyncRun> builder)
    {
        builder.ToTable("SyncRuns");
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).UseIdentityColumn();

        builder.Property(run => run.Trigger).HasConversion<byte>();
        builder.Property(run => run.Status).HasConversion<byte>();
        builder.Property(run => run.FailureReason).HasConversion<byte?>();
        builder.Property(run => run.InstanceId).HasMaxLength(100).IsRequired();
        builder.Property(run => run.Error).HasMaxLength(SyncRun.MaxErrorLength);

        // Requêtes du planificateur et de l'état : dernier run par statut et déclencheur.
        builder.HasIndex(run => new { run.Status, run.Trigger, run.Id }).IsDescending(false, false, true)
            .HasDatabaseName("IX_SyncRuns_Status_Trigger");
    }
}
