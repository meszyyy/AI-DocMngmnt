using Pgvector;

namespace AiDocMngmnt.Data;

// A slice of a document's extracted text together with its embedding vector.
// Semantic search runs against chunks (not whole documents) so a match can
// point at the relevant part of a long document.
public class DocumentChunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Document? Document { get; set; }

    // Position of the chunk within the document (0-based).
    public int Index { get; set; }

    public required string Text { get; set; }

    // 1536 dimensions — the output size of text-embedding-3-small.
    public Vector? Embedding { get; set; }
}
