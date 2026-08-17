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

    private static readonly JsonSerializerOptions NdjsonOptions = new(JsonSerializerDefaults.Web);

    // One NDJSON line, flushed immediately so the browser sees it right away.
    private static async Task WriteLineAsync(HttpResponse response, object payload, CancellationToken ct)
    {
        await response.WriteAsync(JsonSerializer.Serialize(payload, NdjsonOptions) + "\n", ct);
        await response.Body.FlushAsync(ct);
    }

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

        // RAG chat: retrieve the most relevant chunks, hand them to the model as
        // context, and stream the grounded answer back as NDJSON lines
        // ({"type":"sources",...} first, then {"type":"delta","text":...} pieces).
        group.MapPost("/chat", async (ChatRequest request,
                IEmbeddingGenerator<string, Embedding<float>> embedder,
                IChatClient chatClient,
                AppDbContext db,
                HttpContext http) =>
            {
                var ct = http.RequestAborted;

                if (string.IsNullOrWhiteSpace(request.Question))
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                // 1. Retrieval: embed the question and fetch the nearest chunks.
                //    The distance cut-off keeps unrelated text out of the context.
                var embedding = await embedder.GenerateAsync(request.Question, cancellationToken: ct);
                var queryVector = new Vector(embedding.Vector);

                const double MaxDistance = 0.75;

                var hits = await db.Chunks
                    .Select(c => new
                    {
                        c.DocumentId,
                        c.Document!.FileName,
                        c.Text,
                        Distance = c.Embedding!.CosineDistance(queryVector),
                    })
                    .Where(x => x.Distance <= MaxDistance)
                    .OrderBy(x => x.Distance)
                    .Take(6)
                    .ToListAsync(ct);

                http.Response.ContentType = "application/x-ndjson; charset=utf-8";

                // 2. Tell the client up front which sources the answer will use.
                var sources = hits
                    .Select((h, i) => new ChatSource(i + 1, h.DocumentId, h.FileName, 1 - h.Distance))
                    .ToList();
                await WriteLineAsync(http.Response, new { type = "sources", sources }, ct);

                if (hits.Count == 0)
                {
                    await WriteLineAsync(http.Response, new
                    {
                        type = "delta",
                        text = "Nem találtam kapcsolódó tartalmat a dokumentumokban. / " +
                               "I could not find anything related in your documents.",
                    }, ct);
                    return;
                }

                // 3. Augmentation: the retrieved excerpts become the model's context.
                var context = string.Join("\n\n", hits.Select((h, i) => $"[{i + 1}] {h.FileName}:\n{h.Text}"));

                List<ChatMessage> messages =
                [
                    new(ChatRole.System,
                        """
                        You answer questions about the user's documents.
                        Use ONLY the numbered context excerpts below. If the answer is not
                        in the context, say so honestly instead of guessing.
                        Answer in the language of the question.
                        Cite the excerpts you used inline as [1], [2], ...
                        """ + "\n\nContext:\n" + context),
                    new(ChatRole.User, request.Question),
                ];

                // 4. Generation: stream the answer token-by-token to the browser.
                await foreach (var update in chatClient.GetStreamingResponseAsync(messages, cancellationToken: ct))
                {
                    if (!string.IsNullOrEmpty(update.Text))
                    {
                        await WriteLineAsync(http.Response, new { type = "delta", text = update.Text }, ct);
                    }
                }
            })
            .WithName("ChatWithDocuments");

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

public record ChatRequest(string Question);

public record ChatSource(int Index, Guid DocumentId, string FileName, double Score);
