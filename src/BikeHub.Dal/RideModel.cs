using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Dal;

internal static class RideModel
{
    internal static void Configure(ModelBuilder model)
    {
        var ride = model.Entity<GroupRide>();
        ride.ToTable("rides", t =>
        {
            t.HasCheckConstraint("ck_rides_capacity", "capacity IS NULL OR capacity BETWEEN 1 AND 10000");
            t.HasCheckConstraint("ck_rides_distance", "distance_km BETWEEN 0 AND 10000");
            t.HasCheckConstraint("ck_rides_speed", "average_speed_kmh IS NULL OR average_speed_kmh BETWEEN 1 AND 100");
            t.HasCheckConstraint("ck_rides_meeting", "(meeting_lat IS NULL AND meeting_lng IS NULL) OR (meeting_lat IS NOT NULL AND meeting_lng IS NOT NULL AND meeting_lat BETWEEN -90 AND 90 AND meeting_lng BETWEEN -180 AND 180)");
            t.HasCheckConstraint("ck_rides_status", "status IN (1, 2) AND difficulty IN (1, 2, 3) AND revision > 0");
        });
        ride.HasKey(x => x.Id).HasName("pk_rides");
        ride.Property(x => x.Id).ValueGeneratedNever();
        ride.Property(x => x.Title).HasMaxLength(160);
        ride.Property(x => x.Description).HasMaxLength(10000);
        ride.Property(x => x.City).HasMaxLength(100);
        ride.Property(x => x.MeetingName).HasMaxLength(300);
        ride.Property(x => x.TimeZone).HasMaxLength(100);
        ride.Property(x => x.RouteJson).HasColumnType("jsonb");
        ride.HasIndex(x => new { x.StartsAt, x.Id }).HasDatabaseName("ix_rides_upcoming");
        ride.HasIndex(x => new { x.GroupId, x.StartsAt, x.Id }).HasDatabaseName("ix_rides_group");
        ride.HasOne<AuthAccountEntity>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_ride_owner");
        ride.HasOne<CommunityGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_ride_group");
        var participant = model.Entity<RideParticipant>();
        participant.ToTable("ride_participants", t => t.HasCheckConstraint("ck_ride_attendance", "attendance IN (1, 2)"));
        participant.HasKey(x => new { x.RideId, x.UserId }).HasName("pk_ride_participants");
        participant.HasIndex(x => x.UserId).HasDatabaseName("ix_ride_participants_user");
        participant.HasOne<GroupRide>().WithMany().HasForeignKey(x => x.RideId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_ride_participant_ride");
        participant.HasOne<AuthAccountEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_ride_participant_user");
        var change = model.Entity<RideChange>();
        change.ToTable("ride_changes");
        change.HasKey(x => x.Id).HasName("pk_ride_changes");
        change.Property(x => x.Id).ValueGeneratedNever();
        change.HasIndex(x => new { x.RideId, x.Revision }).IsUnique().HasDatabaseName("ix_ride_changes_revision");
        change.HasOne<GroupRide>().WithMany().HasForeignKey(x => x.RideId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_ride_change_ride");
    }
}
