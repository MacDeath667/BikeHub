using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BikeHub.Dal.Migrations;

public partial class RouteLibrary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS library_routes (
                id uuid CONSTRAINT pk_library_routes PRIMARY KEY,
                owner_id uuid NOT NULL CONSTRAINT fk_library_route_owner REFERENCES auth_accounts(id) ON DELETE RESTRICT,
                title varchar(160) NOT NULL, description varchar(10000) NOT NULL, city varchar(100) NOT NULL,
                surface integer NOT NULL, distance_km numeric NOT NULL, route_json jsonb NOT NULL,
                revision bigint NOT NULL, is_hidden boolean NOT NULL, is_deleted boolean NOT NULL,
                created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL,
                CONSTRAINT ck_library_routes_values CHECK (surface IN (1, 2, 3, 4) AND distance_km >= 0 AND revision > 0)
            );
            CREATE TABLE IF NOT EXISTS route_collections (
                id uuid CONSTRAINT pk_route_collections PRIMARY KEY,
                owner_id uuid NOT NULL CONSTRAINT fk_route_collection_owner REFERENCES auth_accounts(id) ON DELETE CASCADE,
                name varchar(80) NOT NULL, normalized_name varchar(80) NOT NULL, created_at timestamptz NOT NULL
            );
            CREATE TABLE IF NOT EXISTS saved_routes (
                route_id uuid NOT NULL CONSTRAINT fk_saved_route_route REFERENCES library_routes(id) ON DELETE CASCADE,
                user_id uuid NOT NULL CONSTRAINT fk_saved_route_user REFERENCES auth_accounts(id) ON DELETE CASCADE,
                collection_id uuid NULL CONSTRAINT fk_saved_route_collection REFERENCES route_collections(id) ON DELETE SET NULL,
                note varchar(2000) NOT NULL, created_at timestamptz NOT NULL,
                CONSTRAINT pk_saved_routes PRIMARY KEY (route_id, user_id)
            );
            CREATE TABLE IF NOT EXISTS route_reviews (
                id uuid CONSTRAINT pk_route_reviews PRIMARY KEY,
                route_id uuid NOT NULL CONSTRAINT fk_route_review_route REFERENCES library_routes(id) ON DELETE CASCADE,
                author_id uuid NOT NULL CONSTRAINT fk_route_review_user REFERENCES auth_accounts(id) ON DELETE CASCADE,
                rating integer NOT NULL, text varchar(2000) NOT NULL, ridden_on date NOT NULL,
                is_hidden boolean NOT NULL, is_deleted boolean NOT NULL, created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL,
                CONSTRAINT ck_route_review_rating CHECK (rating BETWEEN 1 AND 5)
            );
            CREATE INDEX IF NOT EXISTS ix_library_routes_recent ON library_routes(created_at, id);
            CREATE INDEX IF NOT EXISTS ix_library_routes_filter ON library_routes(surface, distance_km, id);
            CREATE INDEX IF NOT EXISTS ix_library_routes_owner ON library_routes(owner_id);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_route_collections_name ON route_collections(owner_id, normalized_name);
            CREATE INDEX IF NOT EXISTS ix_saved_routes_user ON saved_routes(user_id, collection_id);
            CREATE INDEX IF NOT EXISTS ix_saved_routes_collection ON saved_routes(collection_id);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_route_reviews_author ON route_reviews(route_id, author_id);
            CREATE INDEX IF NOT EXISTS ix_route_reviews_recent ON route_reviews(route_id, created_at, id);
            CREATE INDEX IF NOT EXISTS ix_route_reviews_user ON route_reviews(author_id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("This migration is forward-only. Restore a database backup to roll back.");
    }
}
