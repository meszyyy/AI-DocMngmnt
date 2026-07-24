namespace AiDocMngmnt.Data;

// Message contract shared by the API (producer) and the Worker (consumer).
// Keep it small: the message carries only the id, the worker loads the rest
// from the database. This avoids stale data in the queue.
public record ProcessDocumentMessage(Guid DocumentId);

public static class Queues
{
    public const string DocumentsToProcess = "documents-to-process";
}
