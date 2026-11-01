using Microsoft.EntityFrameworkCore.Migrations;

namespace BikeHub.Dal.Migrations;

public partial class AdoptExistingSchema : Migration
{
    private const string RollbackMessage = "This baseline adopts existing tables. Automatic rollback is disabled to preserve their data.";

    private const string InitialSchemaSql = """
        CREATE TABLE IF NOT EXISTS bikes
        (
            id UUID PRIMARY KEY,
            name TEXT NOT NULL,
            description TEXT,
            owner_id UUID NOT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT now()
        );

        CREATE INDEX IF NOT EXISTS ix_bikes_owner_id
            ON bikes (owner_id);
        """;

    private const string UuidV7Sql = """
        DO $migration$
        BEGIN
            IF to_regprocedure('uuidv7()') IS NULL THEN
                -- PostgreSQL versions before 18 do not provide uuidv7() natively.
                -- Build a UUIDv7 using the current Unix timestamp and random UUID bits.
                CREATE FUNCTION uuidv7()
                RETURNS uuid
                LANGUAGE plpgsql
                VOLATILE
                AS $$
                DECLARE
                    unix_ts_ms bigint;
                    uuid_bytes bytea;
                    i integer;
                BEGIN
                    unix_ts_ms := floor(extract(epoch FROM clock_timestamp()) * 1000);
                    uuid_bytes := uuid_send(gen_random_uuid());

                    -- UUIDv7 stores the 48-bit Unix timestamp in milliseconds first.
                    FOR i IN 0..5 LOOP
                        uuid_bytes := set_byte(
                            uuid_bytes,
                            i,
                            ((unix_ts_ms >> ((5 - i) * 8)) & 255)::integer
                        );
                    END LOOP;

                    -- Set version 7 and the RFC 9562 variant bits, preserving random bits.
                    uuid_bytes := set_byte(uuid_bytes, 6, (get_byte(uuid_bytes, 6) & 15) | 112);
                    uuid_bytes := set_byte(uuid_bytes, 8, (get_byte(uuid_bytes, 8) & 63) | 128);

                    RETURN encode(uuid_bytes, 'hex')::uuid;
                END;
                $$;
            END IF;
        END;
        $migration$;


        ALTER TABLE bikes
            ALTER COLUMN id SET DEFAULT uuidv7(),
            ALTER COLUMN created_at SET DEFAULT NOW();
        """;

    private const string AuthenticationSql = """
        CREATE TABLE IF NOT EXISTS auth_accounts
        (
            id UUID PRIMARY KEY,
            email TEXT NOT NULL UNIQUE CHECK (length(email) <= 254),
            display_name TEXT NOT NULL CHECK (length(display_name) BETWEEN 1 AND 60),
            email_confirmed_at TIMESTAMPTZ NOT NULL,
            phone_e164 TEXT NULL,
            phone_confirmed_at TIMESTAMPTZ NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT now()
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ix_auth_accounts_phone ON auth_accounts(phone_e164) WHERE phone_e164 IS NOT NULL;

        CREATE TABLE IF NOT EXISTS auth_email_challenges
        (
            id UUID PRIMARY KEY,
            email TEXT NOT NULL CHECK (length(email) <= 254),
            display_name TEXT NOT NULL CHECK (length(display_name) BETWEEN 1 AND 60),
            code_hash TEXT NOT NULL CHECK (length(code_hash) = 64),
            binding_hash TEXT NOT NULL CHECK (length(binding_hash) = 64),
            created_at TIMESTAMPTZ NOT NULL,
            expires_at TIMESTAMPTZ NOT NULL,
            attempts INTEGER NOT NULL DEFAULT 0 CHECK (attempts >= 0),
            delivered BOOLEAN NOT NULL DEFAULT false,
            consumed BOOLEAN NOT NULL DEFAULT false
        );

        CREATE INDEX IF NOT EXISTS ix_auth_email_challenges_email_created ON auth_email_challenges(email, created_at);
        CREATE INDEX IF NOT EXISTS ix_auth_email_challenges_created ON auth_email_challenges(created_at);
        CREATE INDEX IF NOT EXISTS ix_auth_email_challenges_binding ON auth_email_challenges(binding_hash) WHERE NOT consumed;

        CREATE TABLE IF NOT EXISTS auth_sessions
        (
            id UUID PRIMARY KEY,
            account_id UUID NOT NULL REFERENCES auth_accounts(id) ON DELETE CASCADE,
            created_at TIMESTAMPTZ NOT NULL,
            expires_at TIMESTAMPTZ NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_auth_sessions_account ON auth_sessions(account_id);
        CREATE INDEX IF NOT EXISTS ix_auth_sessions_expires ON auth_sessions(expires_at);
        """;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(InitialSchemaSql);
        migrationBuilder.Sql(UuidV7Sql);
        migrationBuilder.Sql(AuthenticationSql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException(RollbackMessage);
    }
}
