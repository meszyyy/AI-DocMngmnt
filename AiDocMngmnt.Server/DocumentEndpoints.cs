using AiDocMngmnt.Server.Data;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;

namespace AiDocMngmnt.Server;

public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/documents");

        group.MapGet("/", (AppDbContext db) =>
                db.Documents.OrderByDescending(d => d.UploadedAt).ToListAsync())
            .WithName("ListDocuments")
            // Redis-backed output cache: for 5s the response is served from Redis
            // without touching the database.
            .CacheOutput(p => p.Expire(TimeSpan.FromSeconds(5)));

        group.MapGet("/{id:guid}", async Task<IResult> (Guid id, AppDbContext db) =>
                await db.Documents.FindAsync(id) is { } doc
                    ? TypedResults.Ok(doc)
                    : TypedResults.NotFound())
            .WithName("GetDocument");

        // Multipart upload: the file goes to blob storage, its metadata to PostgreSQL.
        group.MapPost("/", async (IFormFile file, BlobContainerClient blobContainer, AppDbContext db) =>
            {
                if (file.Length == 0)
                {
                    return Results.BadRequest("The uploaded file is empty.");
                }

                var doc = new Document
                {
                    // Version 7 (time-ordered) GUID: index-friendly because new rows
                    // always land at the end of the B-tree.
                    Id = Guid.CreateVersion7(),
                    FileName = file.FileName,
                    ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                        ? "application/octet-stream"
                        : file.ContentType,
                    SizeBytes = file.Length,
                };

                // Prefix the blob name with the id so two uploads with the same
                // file name can never collide.
                doc.BlobPath = $"{doc.Id}/{doc.FileName}";

                var blob = blobContainer.GetBlobClient(doc.BlobPath);
                await using (var stream = file.OpenReadStream())
                {
                    await blob.UploadAsync(stream, new BlobUploadOptions
                    {
                        // Stored on the blob itself, so downloads get the right MIME type.
                        HttpHeaders = new BlobHttpHeaders { ContentType = doc.ContentType },
                    });
                }

                // Metadata is saved only after the blob upload succeeded, so the DB
                // never references a blob that does not exist.
                db.Documents.Add(doc);
                await db.SaveChangesAsync();

                return Results.Created($"/api/documents/{doc.Id}", doc);
            })
            .WithName("UploadDocument")
            // Browser form posts normally require an antiforgery token; this API is
            // consumed with fetch() where that protection model does not apply.
            .DisableAntiforgery();

        // Streams the file back from blob storage with its original name and type.
        group.MapGet("/{id:guid}/content", async Task<IResult> (Guid id, BlobContainerClient blobContainer, AppDbContext db) =>
            {
                var doc = await db.Documents.FindAsync(id);
                if (doc?.BlobPath is null)
                {
                    return TypedResults.NotFound();
                }

                var blob = blobContainer.GetBlobClient(doc.BlobPath);
                if (!await blob.ExistsAsync())
                {
                    return TypedResults.NotFound();
                }

                // OpenReadAsync streams the blob; the file is never buffered
                // fully in server memory.
                var stream = await blob.OpenReadAsync();
                return TypedResults.Stream(stream, doc.ContentType, doc.FileName);
            })
            .WithName("DownloadDocument");

        group.MapDelete("/{id:guid}", async Task<IResult> (Guid id, BlobContainerClient blobContainer, AppDbContext db) =>
            {
                var doc = await db.Documents.FindAsync(id);
                if (doc is null)
                {
                    return TypedResults.NotFound();
                }

                if (doc.BlobPath is not null)
                {
                    await blobContainer.DeleteBlobIfExistsAsync(doc.BlobPath);
                }

                db.Documents.Remove(doc);
                await db.SaveChangesAsync();

                return TypedResults.NoContent();
            })
            .WithName("DeleteDocument");

        return api;
    }
}
