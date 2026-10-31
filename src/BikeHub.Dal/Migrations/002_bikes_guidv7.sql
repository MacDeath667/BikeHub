-- PostgreSQL versions before 18 do not provide uuidv7() natively.
-- Build a UUIDv7 using the current Unix timestamp and random UUID bits.
CREATE OR REPLACE FUNCTION public.uuidv7()
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

ALTER TABLE bikes
    ALTER COLUMN id SET DEFAULT public.uuidv7(),
    ALTER COLUMN created_at SET DEFAULT NOW();
