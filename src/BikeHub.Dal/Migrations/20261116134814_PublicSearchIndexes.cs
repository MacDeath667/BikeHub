using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BikeHub.Dal.Migrations
{
    /// <inheritdoc />
    public partial class PublicSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                SELECT pg_advisory_xact_lock(1112099659);
                CREATE EXTENSION IF NOT EXISTS pg_trgm WITH SCHEMA public;
                DO $migration$
                DECLARE trigram_schema text;
                BEGIN
                    SELECT n.nspname INTO STRICT trigram_schema
                      FROM pg_extension e JOIN pg_namespace n ON n.oid = e.extnamespace
                     WHERE e.extname = 'pg_trgm';
                    EXECUTE format($index$
                        CREATE INDEX IF NOT EXISTS ix_posts_search ON posts USING gin
                        ((title || ' ' || content) %I.gin_trgm_ops)
                        WHERE NOT is_deleted AND NOT is_hidden
                    $index$, trigram_schema);
                    EXECUTE format($index$
                        CREATE INDEX IF NOT EXISTS ix_accounts_search ON auth_accounts USING gin
                        ((display_name || ' ' || coalesce(handle, '') || ' ' || city) %I.gin_trgm_ops)
                        WHERE NOT is_blocked
                    $index$, trigram_schema);
                    EXECUTE format($index$
                        CREATE INDEX IF NOT EXISTS ix_communities_search ON communities USING gin
                        ((name || ' ' || description) %I.gin_trgm_ops)
                        WHERE NOT is_deleted AND NOT is_hidden
                    $index$, trigram_schema);
                    EXECUTE format($index$
                        CREATE INDEX IF NOT EXISTS ix_group_entries_search ON group_entries USING gin
                        ((title || ' ' || content) %I.gin_trgm_ops)
                        WHERE NOT is_deleted AND NOT is_hidden
                    $index$, trigram_schema);
                END $migration$;
                CREATE INDEX IF NOT EXISTS ix_group_entries_kind_feed
                    ON group_entries(group_id, kind, created_at DESC, id DESC)
                    WHERE NOT is_deleted AND NOT is_hidden;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("This migration is forward-only. Restore a database backup to roll back.");
        }
    }
}
