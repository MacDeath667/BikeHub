using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BikeHub.Dal.Migrations;

public partial class BikeMaintenance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS bike_components (
                id uuid CONSTRAINT pk_bike_components PRIMARY KEY,
                bike_id uuid NOT NULL CONSTRAINT fk_bike_component_bike REFERENCES bikes(id) ON DELETE CASCADE,
                kind integer NOT NULL, name varchar(100) NOT NULL, model varchar(100) NOT NULL, notes varchar(2000) NOT NULL,
                installed_mileage numeric NOT NULL, installed_on date NOT NULL, service_interval_km numeric NULL,
                is_archived boolean NOT NULL, revision bigint NOT NULL, created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL,
                CONSTRAINT ck_bike_components_values CHECK (installed_mileage BETWEEN 0 AND 1000000 AND
                    (service_interval_km IS NULL OR service_interval_km BETWEEN 1 AND 100000) AND kind BETWEEN 1 AND 9 AND revision > 0)
            );
            CREATE TABLE IF NOT EXISTS maintenance_records (
                id uuid CONSTRAINT pk_maintenance_records PRIMARY KEY,
                bike_id uuid NOT NULL CONSTRAINT fk_maintenance_record_bike REFERENCES bikes(id) ON DELETE CASCADE,
                component_id uuid NULL CONSTRAINT fk_maintenance_record_component REFERENCES bike_components(id) ON DELETE SET NULL,
                kind integer NOT NULL, title varchar(160) NOT NULL, notes varchar(4000) NOT NULL, performed_on date NOT NULL,
                mileage numeric NOT NULL, cost_rub numeric NULL, resets_interval boolean NOT NULL, is_deleted boolean NOT NULL,
                revision bigint NOT NULL, created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL,
                CONSTRAINT ck_maintenance_records_values CHECK (mileage BETWEEN 0 AND 1000000 AND
                    (cost_rub IS NULL OR cost_rub BETWEEN 0 AND 10000000) AND kind BETWEEN 1 AND 6 AND revision > 0)
            );
            CREATE TABLE IF NOT EXISTS maintenance_reminders (
                id uuid CONSTRAINT pk_maintenance_reminders PRIMARY KEY,
                component_id uuid NOT NULL CONSTRAINT fk_maintenance_reminder_component REFERENCES bike_components(id) ON DELETE CASCADE,
                due_mileage numeric NOT NULL, created_at timestamptz NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_bike_components_bike ON bike_components(bike_id, is_archived, id);
            CREATE INDEX IF NOT EXISTS ix_maintenance_records_history ON maintenance_records(bike_id, performed_on, id);
            CREATE INDEX IF NOT EXISTS ix_maintenance_records_reset ON maintenance_records(component_id, mileage) WHERE resets_interval AND NOT is_deleted;
            CREATE UNIQUE INDEX IF NOT EXISTS ix_maintenance_reminders_cycle ON maintenance_reminders(component_id, due_mileage);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("This migration is forward-only. Restore a database backup to roll back.");
    }
}
