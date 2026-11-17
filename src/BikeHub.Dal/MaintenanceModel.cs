using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Dal;

internal static class MaintenanceModel
{
    internal static void Configure(ModelBuilder model)
    {
        var component = model.Entity<BikeComponent>();
        component.ToTable("bike_components", t => t.HasCheckConstraint("ck_bike_components_values", "installed_mileage BETWEEN 0 AND 1000000 AND (service_interval_km IS NULL OR service_interval_km BETWEEN 1 AND 100000) AND kind BETWEEN 1 AND 9 AND revision > 0"));
        component.HasKey(x => x.Id).HasName("pk_bike_components");
        component.Property(x => x.Id).ValueGeneratedNever();
        component.Property(x => x.Name).HasMaxLength(100);
        component.Property(x => x.Model).HasMaxLength(100);
        component.Property(x => x.Notes).HasMaxLength(2000);
        component.HasIndex(x => new { x.BikeId, x.IsArchived, x.Id }).HasDatabaseName("ix_bike_components_bike");
        component.HasOne<Bike>().WithMany().HasForeignKey(x => x.BikeId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_bike_component_bike");
        var record = model.Entity<MaintenanceRecord>();
        record.ToTable("maintenance_records", t => t.HasCheckConstraint("ck_maintenance_records_values", "mileage BETWEEN 0 AND 1000000 AND (cost_rub IS NULL OR cost_rub BETWEEN 0 AND 10000000) AND kind BETWEEN 1 AND 6 AND revision > 0"));
        record.HasKey(x => x.Id).HasName("pk_maintenance_records");
        record.Property(x => x.Id).ValueGeneratedNever();
        record.Property(x => x.Title).HasMaxLength(160);
        record.Property(x => x.Notes).HasMaxLength(4000);
        record.HasIndex(x => new { x.BikeId, x.PerformedOn, x.Id }).HasDatabaseName("ix_maintenance_records_history");
        record.HasIndex(x => new { x.ComponentId, x.Mileage }).HasDatabaseName("ix_maintenance_records_reset").HasFilter("resets_interval AND NOT is_deleted");
        record.HasOne<Bike>().WithMany().HasForeignKey(x => x.BikeId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_maintenance_record_bike");
        record.HasOne<BikeComponent>().WithMany().HasForeignKey(x => x.ComponentId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_maintenance_record_component");
        var reminder = model.Entity<MaintenanceReminder>();
        reminder.ToTable("maintenance_reminders");
        reminder.HasKey(x => x.Id).HasName("pk_maintenance_reminders");
        reminder.Property(x => x.Id).ValueGeneratedNever();
        reminder.HasIndex(x => new { x.ComponentId, x.DueMileage }).IsUnique().HasDatabaseName("ix_maintenance_reminders_cycle");
        reminder.HasOne<BikeComponent>().WithMany().HasForeignKey(x => x.ComponentId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_maintenance_reminder_component");
    }
}
