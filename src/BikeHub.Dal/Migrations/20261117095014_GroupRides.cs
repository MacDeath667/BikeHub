using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BikeHub.Dal.Migrations;

public partial class GroupRides : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS rides (
                id uuid CONSTRAINT pk_rides PRIMARY KEY,
                owner_id uuid NOT NULL CONSTRAINT fk_ride_owner REFERENCES auth_accounts(id) ON DELETE RESTRICT,
                group_id uuid NULL CONSTRAINT fk_ride_group REFERENCES communities(id) ON DELETE SET NULL,
                title varchar(160) NOT NULL, description varchar(10000) NOT NULL, city varchar(100) NOT NULL,
                meeting_name varchar(300) NOT NULL, meeting_lat double precision NULL, meeting_lng double precision NULL,
                starts_at timestamptz NOT NULL, time_zone varchar(100) NOT NULL,
                distance_km numeric NOT NULL, average_speed_kmh numeric NULL, difficulty integer NOT NULL,
                capacity integer NULL, route_json jsonb NULL, status integer NOT NULL, revision bigint NOT NULL,
                is_hidden boolean NOT NULL, is_deleted boolean NOT NULL, created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL,
                CONSTRAINT ck_rides_capacity CHECK (capacity IS NULL OR capacity BETWEEN 1 AND 10000),
                CONSTRAINT ck_rides_distance CHECK (distance_km BETWEEN 0 AND 10000),
                CONSTRAINT ck_rides_speed CHECK (average_speed_kmh IS NULL OR average_speed_kmh BETWEEN 1 AND 100),
                CONSTRAINT ck_rides_status CHECK (status IN (1, 2) AND difficulty IN (1, 2, 3) AND revision > 0),
                CONSTRAINT ck_rides_meeting CHECK ((meeting_lat IS NULL AND meeting_lng IS NULL) OR
                    (meeting_lat IS NOT NULL AND meeting_lng IS NOT NULL AND meeting_lat BETWEEN -90 AND 90 AND meeting_lng BETWEEN -180 AND 180))
            );
            CREATE TABLE IF NOT EXISTS ride_participants (
                ride_id uuid NOT NULL CONSTRAINT fk_ride_participant_ride REFERENCES rides(id) ON DELETE CASCADE,
                user_id uuid NOT NULL CONSTRAINT fk_ride_participant_user REFERENCES auth_accounts(id) ON DELETE CASCADE,
                attendance integer NOT NULL, created_at timestamptz NOT NULL,
                CONSTRAINT pk_ride_participants PRIMARY KEY (ride_id, user_id),
                CONSTRAINT ck_ride_attendance CHECK (attendance IN (1, 2))
            );
            CREATE TABLE IF NOT EXISTS ride_changes (
                id uuid CONSTRAINT pk_ride_changes PRIMARY KEY,
                ride_id uuid NOT NULL CONSTRAINT fk_ride_change_ride REFERENCES rides(id) ON DELETE CASCADE,
                revision bigint NOT NULL, created_at timestamptz NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_rides_upcoming ON rides(starts_at, id);
            CREATE INDEX IF NOT EXISTS ix_rides_group ON rides(group_id, starts_at, id);
            CREATE INDEX IF NOT EXISTS "IX_rides_owner_id" ON rides(owner_id);
            CREATE INDEX IF NOT EXISTS ix_ride_participants_user ON ride_participants(user_id);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_ride_changes_revision ON ride_changes(ride_id, revision);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("This migration is forward-only. Restore a database backup to roll back.");
    }
}
