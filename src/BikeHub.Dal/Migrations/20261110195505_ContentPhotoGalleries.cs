using Microsoft.EntityFrameworkCore.Migrations;

namespace BikeHub.Dal.Migrations;

public partial class ContentPhotoGalleries : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE bikes ADD COLUMN IF NOT EXISTS image_ids uuid[] NOT NULL DEFAULT ARRAY[]::uuid[];
            ALTER TABLE posts ADD COLUMN IF NOT EXISTS image_ids uuid[] NOT NULL DEFAULT ARRAY[]::uuid[];
            UPDATE bikes SET image_ids = ARRAY[image_id] WHERE image_id IS NOT NULL AND cardinality(image_ids) = 0;
            UPDATE posts SET image_ids = ARRAY[image_id] WHERE image_id IS NOT NULL AND cardinality(image_ids) = 0;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Content migrations are forward-only.");
    }
}
