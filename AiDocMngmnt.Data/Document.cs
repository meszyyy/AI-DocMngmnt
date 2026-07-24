namespace AiDocMngmnt.Data;

public class Document
{
    public Guid Id { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Uploaded;
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;

    // Actual location of the file in blob storage (filled in Phase 3).
    public string? BlobPath { get; set; }

    // Raw text extracted from the file (input for AI analysis and,
    // later, for semantic search chunking).
    public string? ExtractedText { get; set; }

    // AI-generated fields.
    public string? Summary { get; set; }
    public List<string> Tags { get; set; } = [];
}

public enum DocumentStatus
{
    Uploaded,
    Processing,
    Processed,
    Failed
}

