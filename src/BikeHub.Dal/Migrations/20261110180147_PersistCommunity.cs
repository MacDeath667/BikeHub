using Microsoft.EntityFrameworkCore.Migrations;

namespace BikeHub.Dal.Migrations;

public partial class PersistCommunity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE bikes ADD COLUMN IF NOT EXISTS brand character varying(80) NOT NULL DEFAULT '';
            
            ALTER TABLE bikes ADD COLUMN IF NOT EXISTS category integer NOT NULL DEFAULT 3;
            
            ALTER TABLE bikes ADD COLUMN IF NOT EXISTS image_id uuid;
            
            ALTER TABLE bikes ADD COLUMN IF NOT EXISTS is_deleted boolean NOT NULL DEFAULT FALSE;
            
            ALTER TABLE bikes ADD COLUMN IF NOT EXISTS is_hidden boolean NOT NULL DEFAULT FALSE;
            
            ALTER TABLE bikes ADD COLUMN IF NOT EXISTS mileage numeric NOT NULL DEFAULT 0.0;
            
            ALTER TABLE bikes ADD COLUMN IF NOT EXISTS year integer NOT NULL DEFAULT 0;
            
            ALTER TABLE auth_accounts ADD COLUMN IF NOT EXISTS avatar_id uuid;
            
            ALTER TABLE auth_accounts ADD COLUMN IF NOT EXISTS bio character varying(2000) NOT NULL DEFAULT '';
            
            ALTER TABLE auth_accounts ADD COLUMN IF NOT EXISTS city character varying(100) NOT NULL DEFAULT '';
            
            ALTER TABLE auth_accounts ADD COLUMN IF NOT EXISTS handle character varying(32);
            
            ALTER TABLE auth_accounts ADD COLUMN IF NOT EXISTS is_blocked boolean NOT NULL DEFAULT FALSE;
            
            CREATE TABLE IF NOT EXISTS communities (
                id uuid NOT NULL,
                name character varying(100) NOT NULL,
                description character varying(2000) NOT NULL,
                category integer NOT NULL,
                image_url text NOT NULL,
                CONSTRAINT pk_communities PRIMARY KEY (id)
            );
            
            CREATE TABLE IF NOT EXISTS community_relations (
                user_id uuid NOT NULL,
                target_id uuid NOT NULL,
                kind integer NOT NULL,
                CONSTRAINT pk_community_relations PRIMARY KEY (user_id, target_id, kind),
                CONSTRAINT fk_relation_user FOREIGN KEY (user_id) REFERENCES auth_accounts (id) ON DELETE RESTRICT
            );
            
            CREATE TABLE IF NOT EXISTS media_assets (
                id uuid NOT NULL,
                owner_id uuid NOT NULL,
                width integer NOT NULL,
                height integer NOT NULL,
                bytes bigint NOT NULL,
                created_at timestamp with time zone NOT NULL,
                is_deleted boolean NOT NULL,
                CONSTRAINT pk_media_assets PRIMARY KEY (id),
                CONSTRAINT fk_media_owner FOREIGN KEY (owner_id) REFERENCES auth_accounts (id) ON DELETE RESTRICT
            );
            
            CREATE TABLE IF NOT EXISTS moderation_reports (
                id uuid NOT NULL,
                reporter_id uuid NOT NULL,
                target_type integer NOT NULL,
                target_id uuid NOT NULL,
                reason character varying(2000) NOT NULL,
                created_at timestamp with time zone NOT NULL,
                resolved boolean NOT NULL,
                resolved_by uuid,
                resolution character varying(2000),
                CONSTRAINT pk_moderation_reports PRIMARY KEY (id),
                CONSTRAINT fk_report_reporter FOREIGN KEY (reporter_id) REFERENCES auth_accounts (id) ON DELETE RESTRICT
            );
            
            CREATE TABLE IF NOT EXISTS posts (
                id uuid NOT NULL,
                bike_id uuid NOT NULL,
                author_id uuid NOT NULL,
                title character varying(160) NOT NULL,
                content character varying(20000) NOT NULL,
                tags text[] NOT NULL DEFAULT (ARRAY[]::text[]),
                category character varying(20) NOT NULL,
                image_id uuid,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone,
                is_deleted boolean NOT NULL,
                is_hidden boolean NOT NULL,
                CONSTRAINT pk_posts PRIMARY KEY (id),
                CONSTRAINT fk_post_author FOREIGN KEY (author_id) REFERENCES auth_accounts (id) ON DELETE RESTRICT,
                CONSTRAINT fk_post_bike FOREIGN KEY (bike_id) REFERENCES bikes (id) ON DELETE RESTRICT,
                CONSTRAINT fk_post_image FOREIGN KEY (image_id) REFERENCES media_assets (id) ON DELETE RESTRICT
            );
            
            CREATE TABLE IF NOT EXISTS comments (
                id uuid NOT NULL,
                post_id uuid NOT NULL,
                author_id uuid NOT NULL,
                text character varying(2000) NOT NULL,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone,
                is_deleted boolean NOT NULL,
                is_hidden boolean NOT NULL,
                CONSTRAINT pk_comments PRIMARY KEY (id),
                CONSTRAINT fk_comment_author FOREIGN KEY (author_id) REFERENCES auth_accounts (id) ON DELETE RESTRICT,
                CONSTRAINT fk_comment_post FOREIGN KEY (post_id) REFERENCES posts (id) ON DELETE RESTRICT
            );
            
            CREATE INDEX IF NOT EXISTS "IX_bikes_image_id" ON bikes (image_id);
            
            CREATE INDEX IF NOT EXISTS "IX_auth_accounts_avatar_id" ON auth_accounts (avatar_id);
            
            CREATE UNIQUE INDEX IF NOT EXISTS ix_auth_accounts_handle ON auth_accounts (handle) WHERE handle IS NOT NULL;
            
            CREATE INDEX IF NOT EXISTS "IX_comments_author_id" ON comments (author_id);
            
            CREATE INDEX IF NOT EXISTS ix_comments_post ON comments (post_id, created_at);
            
            CREATE INDEX IF NOT EXISTS ix_relations_target ON community_relations (target_id, kind);
            
            CREATE INDEX IF NOT EXISTS ix_media_owner ON media_assets (owner_id, created_at);
            
            CREATE UNIQUE INDEX IF NOT EXISTS ix_reports_open ON moderation_reports (reporter_id, target_type, target_id) WHERE NOT resolved;
            
            CREATE INDEX IF NOT EXISTS "IX_posts_author_id" ON posts (author_id);
            
            CREATE INDEX IF NOT EXISTS "IX_posts_bike_id" ON posts (bike_id);
            
            CREATE INDEX IF NOT EXISTS ix_posts_feed ON posts (created_at, id);
            
            CREATE INDEX IF NOT EXISTS "IX_posts_image_id" ON posts (image_id);
            
            DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_account_avatar' AND conrelid = 'auth_accounts'::regclass) THEN ALTER TABLE auth_accounts ADD CONSTRAINT fk_account_avatar FOREIGN KEY (avatar_id) REFERENCES media_assets (id) ON DELETE RESTRICT; END IF; END $$;
            
            DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_bike_image' AND conrelid = 'bikes'::regclass) THEN ALTER TABLE bikes ADD CONSTRAINT fk_bike_image FOREIGN KEY (image_id) REFERENCES media_assets (id) ON DELETE RESTRICT; END IF; END $$;
            
            
            INSERT INTO communities (id, name, description, category, image_url) VALUES
            ('b1000000-0000-4000-8000-000000000001', 'Шоссейные поездки', 'Маршруты, тренировки и совместные выезды.', 1, '/images/road.jpg'),
            ('b1000000-0000-4000-8000-000000000002', 'MTB', 'Трейлы, техника и горные велосипеды.', 2, '/images/trail.jpg'),
            ('b1000000-0000-4000-8000-000000000003', 'Гравий', 'Приключения за пределами асфальта.', 3, '/images/gravel.jpg'),
            ('b1000000-0000-4000-8000-000000000004', 'Город', 'Велосипед как часть повседневной жизни.', 4, '/images/city.jpg')
            ON CONFLICT DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Community data migrations are forward-only.");
    }
}
