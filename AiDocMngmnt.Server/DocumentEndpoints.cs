using System.Text.Json;
using AiDocMngmnt.Data;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace AiDocMngmnt.Server;

public static class DocumentEndpoints
{
    // Cache entries carrying this tag are evicted whenever a write happens,
    // so the cached list can never outlive the truth.
    private const string DocumentsCacheTag = "documents";

    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/documents");

        group.MapGet("/", (AppDbContext db) =>
                db.Documents.OrderByDescending(d => d.UploadedAt).ToListAsync())
            .WithName("ListDocuments")
            // Redis-backed output cache: for 5s the response is served from Redis
            // without touching the database.
            .CacheOutput(p => p.Expire(TimeSpan.FromSeconds(5)).Tag(DocumentsCacheTag));

        group.MapGet("/{id:guid}", async Task<IResult> (Guid id, AppDbContext db) =>
                await db.Documents.FindAsync(id) is { } doc
                    ? TypedResults.Ok(doc)
                    : TypedResults.NotFound())
            .WithName("GetDocument");

        // Hybrid-lite search: a chunk is returned when it contains the query
        // literally (names, ids — where embeddings are weak) OR when it is a
        // strong semantic match. Nearest-neighbor alone would always return
        // top-N results no matter how irrelevant they are.
        group.MapGet("/search", async (string q, IEmbeddingGenerator<string, Embedding<float>> embedder, AppDbContext db) =>
            {
                if (string.IsNullOrWhiteSpace(q))
                {
                    return Results.BadRequest("Query must not be empty.");
                }

                var embedding = await embedder.GenerateAsync(q);
                var queryVector = new Vector(embedding.Vector);
                var pattern = $"%{q.Trim()}%";

                // Cosine distance 0 = identical direction; 1 - distance gives an
                // intuitive "higher is better" similarity score. 0.7 distance
                // (score 0.3) is an empirical relevance cut-off for this model.
                const double MaxDistance = 0.7;

                var results = await db.Chunks
                    .Select(c => new
                    {
                        Chunk = c,
                        Distance = c.Embedding!.CosineDistance(queryVector),
                        LiteralMatch = EF.Functions.ILike(c.Text, pattern),
                    })
                    .Where(x => x.LiteralMatch || x.Distance <= MaxDistance)
                    // Literal hits first, then by semantic closeness.
                    .OrderByDescending(x => x.LiteralMatch)
                    .ThenBy(x => x.Distance)
                    .Take(5)
                    .Select(x => new SearchResult(
                        x.Chunk.DocumentId,
                        x.Chunk.Document!.FileName,
                        x.Chunk.Text.Length > 300 ? x.Chunk.Text.Substring(0, 300) + "…" : x.Chunk.Text,
                        1 - x.Distance))
                    .ToListAsync();

                return Results.Ok(results);
            })
            .WithName("SearchDocuments");

        // Multipart upload: the file goes to blob storage, its metadata to PostgreSQL,
        // then a message is queued so the worker can process it asynchronously.
        group.MapPost("/", async (IFormFile file, BlobContainerClient blobContainer, AppDbContext db,
                ServiceBusSender queue, IOutputCacheStore cache) =>
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

                // Hand the heavy work to the worker via the queue. The API stays fast:
                // it only records "something to do" and returns immediately.
                var message = new ServiceBusMessage(JsonSerializer.Serialize(new ProcessDocumentMessage(doc.Id)))
                {
                    ContentType = "application/json",
                    // Stable id enables duplicate detection if we ever turn it on.
                    MessageId = doc.Id.ToString(),
                };
                await queue.SendMessageAsync(message);

                // The world changed: drop the cached list so the next GET is fresh.
                await cache.EvictByTagAsync(DocumentsCacheTag, CancellationToken.None);

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

        group.MapDelete("/{id:guid}", async Task<IResult> (Guid id, BlobContainerClient blobContainer,
                AppDbContext db, IOutputCacheStore cache) =>
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

                await cache.EvictByTagAsync(DocumentsCacheTag, CancellationToken.None);

                return TypedResults.NoContent();
            })
            .WithName("DeleteDocument");

        return api;
    }
}

public record SearchResult(Guid DocumentId, string FileName, string Snippet, double Score);
