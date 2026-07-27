using Microsoft.EntityFrameworkCore;

namespace AiDocMngmnt.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentChunk> Chunks => Set<DocumentChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Enables the pgvector extension (CREATE EXTENSION vector) via migration.
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<Document>(entity =>
        {
            entity.Property(d => d.FileName).HasMaxLength(512);
            entity.Property(d => d.ContentType).HasMaxLength(128);

            // Store the enum as text: the DB holds a readable "Uploaded" instead of
            // a fragile ordinal that would shift if the enum is ever reordered.
            entity.Property(d => d.Status).HasConversion<string>().HasMaxLength(32);

            entity.HasIndex(d => d.UploadedAt);
        });

        modelBuilder.Entity<DocumentChunk>(entity =>
        {
            entity.Property(c => c.Embedding).HasColumnType("vector(1536)");

            // HNSW: approximate-nearest-neighbor index so similarity search stays
            // fast even with many chunks. Cosine distance matches OpenAI embeddings.
            entity.HasIndex(c => c.Embedding)
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops");

            // Deleting a document removes its chunks too.
            entity.HasOne(c => c.Document)
                .WithMany()
                .HasForeignKey(c => c.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
