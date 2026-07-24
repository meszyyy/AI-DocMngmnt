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

    // AI-generated fields (filled in Phase 5).
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

