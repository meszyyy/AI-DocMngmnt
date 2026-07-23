using AiDocMngmnt.Server.Data;
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

        // Until Phase 3 this only creates metadata; it becomes a real file upload there.
        group.MapPost("/", async (CreateDocumentRequest request, AppDbContext db) =>
        {
            var doc = new Document
            {
                // Version 7 (time-ordered) GUID: index-friendly because new rows
                // always land at the end of the B-tree.
                Id = Guid.CreateVersion7(),
                FileName = request.FileName,
                ContentType = "application/octet-stream",
            };

            db.Documents.Add(doc);
            await db.SaveChangesAsync();

            return TypedResults.Created($"/api/documents/{doc.Id}", doc);
        }).WithName("CreateDocument");

        group.MapDelete("/{id:guid}", async Task<IResult> (Guid id, AppDbContext db) =>
                await db.Documents.Where(d => d.Id == id).ExecuteDeleteAsync() > 0
                    ? TypedResults.NoContent()
                    : TypedResults.NotFound())
            .WithName("DeleteDocument");

        return api;
    }
}

public record CreateDocumentRequest(string FileName);
