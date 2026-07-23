using Microsoft.EntityFrameworkCore;

namespace AiDocMngmnt.Server.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Document>(entity =>
        {
            entity.Property(d => d.FileName).HasMaxLength(512);
            entity.Property(d => d.ContentType).HasMaxLength(128);

            // Store the enum as text: the DB holds a readable "Uploaded" instead of
            // a fragile ordinal that would shift if the enum is ever reordered.
            entity.Property(d => d.Status).HasConversion<string>().HasMaxLength(32);

            entity.HasIndex(d => d.UploadedAt);
        });
    }
}
