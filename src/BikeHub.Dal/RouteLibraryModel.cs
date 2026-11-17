using BikeHub.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace BikeHub.Dal;

internal static class RouteLibraryModel
{
    internal static void Configure(ModelBuilder model)
    {
        var route = model.Entity<LibraryRoute>();
        route.ToTable("library_routes", t => t.HasCheckConstraint("ck_library_routes_values", "surface IN (1, 2, 3, 4) AND distance_km >= 0 AND revision > 0"));
        route.HasKey(x => x.Id).HasName("pk_library_routes");
        route.Property(x => x.Id).ValueGeneratedNever();
        route.Property(x => x.Title).HasMaxLength(160);
        route.Property(x => x.Description).HasMaxLength(10000);
        route.Property(x => x.City).HasMaxLength(100);
        route.Property(x => x.RouteJson).HasColumnType("jsonb");
        route.HasIndex(x => new { x.CreatedAt, x.Id }).HasDatabaseName("ix_library_routes_recent");
        route.HasIndex(x => new { x.Surface, x.DistanceKm, x.Id }).HasDatabaseName("ix_library_routes_filter");
        route.HasIndex(x => x.OwnerId).HasDatabaseName("ix_library_routes_owner");
        route.HasOne<AuthAccountEntity>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_library_route_owner");
        var collection = model.Entity<RouteCollection>();
        collection.ToTable("route_collections");
        collection.HasKey(x => x.Id).HasName("pk_route_collections");
        collection.Property(x => x.Id).ValueGeneratedNever();
        collection.Property(x => x.Name).HasMaxLength(80);
        collection.Property(x => x.NormalizedName).HasMaxLength(80);
        collection.HasIndex(x => new { x.OwnerId, x.NormalizedName }).IsUnique().HasDatabaseName("ix_route_collections_name");
        collection.HasOne<AuthAccountEntity>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_route_collection_owner");
        var saved = model.Entity<SavedRoute>();
        saved.ToTable("saved_routes");
        saved.HasKey(x => new { x.RouteId, x.UserId }).HasName("pk_saved_routes");
        saved.Property(x => x.Note).HasMaxLength(2000);
        saved.HasIndex(x => new { x.UserId, x.CollectionId }).HasDatabaseName("ix_saved_routes_user");
        saved.HasIndex(x => x.CollectionId).HasDatabaseName("ix_saved_routes_collection");
        saved.HasOne<LibraryRoute>().WithMany().HasForeignKey(x => x.RouteId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_saved_route_route");
        saved.HasOne<AuthAccountEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_saved_route_user");
        saved.HasOne<RouteCollection>().WithMany().HasForeignKey(x => x.CollectionId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_saved_route_collection");
        var review = model.Entity<RouteReview>();
        review.ToTable("route_reviews", t => t.HasCheckConstraint("ck_route_review_rating", "rating BETWEEN 1 AND 5"));
        review.HasKey(x => x.Id).HasName("pk_route_reviews");
        review.Property(x => x.Id).ValueGeneratedNever();
        review.Property(x => x.Text).HasMaxLength(2000);
        review.HasIndex(x => new { x.RouteId, x.AuthorId }).IsUnique().HasDatabaseName("ix_route_reviews_author");
        review.HasIndex(x => new { x.RouteId, x.CreatedAt, x.Id }).HasDatabaseName("ix_route_reviews_recent");
        review.HasIndex(x => x.AuthorId).HasDatabaseName("ix_route_reviews_user");
        review.HasOne<LibraryRoute>().WithMany().HasForeignKey(x => x.RouteId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_route_review_route");
        review.HasOne<AuthAccountEntity>().WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_route_review_user");
    }
}
