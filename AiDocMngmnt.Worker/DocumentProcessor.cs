using AiDocMngmnt.Data;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Pgvector;

namespace AiDocMngmnt.Worker;

public class DocumentProcessor(
    ServiceBusClient serviceBusClient,
    IServiceScopeFactory scopeFactory,
    ILogger<DocumentProcessor> logger) : BackgroundService
{
    // After this many failed attempts we stop retrying and dead-letter the message.
    private const int MaxAttempts = 3;

    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = serviceBusClient.CreateProcessor(Queues.DocumentsToProcess, new ServiceBusProcessorOptions
        {
            // PeekLock mode: the message stays locked (not deleted) while we work.
            // We complete it explicitly only after successful processing.
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 1,
        });

        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var message = args.Message.Body.ToObjectFromJson<ProcessDocumentMessage>();
        if (message is null)
        {
            // Malformed message: retrying cannot fix it, send it to the dead-letter queue.
            await args.DeadLetterMessageAsync(args.Message, "InvalidBody", "Body is not a ProcessDocumentMessage");
            return;
        }

        logger.LogInformation("Processing document {DocumentId} (delivery attempt {Attempt})",
            message.DocumentId, args.Message.DeliveryCount);

        // BackgroundService is a singleton, DbContext is scoped — so we create
        // a scope per message instead of holding one context forever.
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var document = await db.Documents.FindAsync([message.DocumentId], args.CancellationToken);

        // Idempotency guards: with at-least-once delivery the same message can
        // arrive twice. Processing must be safe to repeat (or skip).
        if (document is null)
        {
            logger.LogWarning("Document {DocumentId} not found (deleted?), completing message", message.DocumentId);
            await args.CompleteMessageAsync(args.Message);
            return;
        }

        if (document.Status == DocumentStatus.Processed)
        {
            logger.LogInformation("Document {DocumentId} already processed, completing message", message.DocumentId);
            await args.CompleteMessageAsync(args.Message);
            return;
        }

        try
        {
            document.Status = DocumentStatus.Processing;
            await db.SaveChangesAsync(args.CancellationToken);

            var blobContainer = scope.ServiceProvider.GetRequiredService<BlobContainerClient>();
            var analyzer = scope.ServiceProvider.GetRequiredService<DocumentAnalyzer>();

            // 1. Download the file from blob storage.
            var blob = blobContainer.GetBlobClient(document.BlobPath);
            await using var content = await blob.OpenReadAsync(cancellationToken: args.CancellationToken);

            // 2. Extract plain text from it.
            var text = await TextExtractor.ExtractAsync(content, document.ContentType, args.CancellationToken);

            if (!string.IsNullOrWhiteSpace(text))
            {
                document.ExtractedText = text;

                // 3. Ask the model for a summary and tags (one chat call).
                var analysis = await analyzer.AnalyzeAsync(document.FileName, text, args.CancellationToken);
                document.Summary = analysis.Summary;
                document.Tags = [.. analysis.Tags];

                // 4. Chunk the text and embed every chunk (one batched call),
                //    replacing any chunks from a previous processing run.
                var embeddingGenerator = scope.ServiceProvider
                    .GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

                var chunkTexts = TextChunker.Chunk(text);
                var embeddings = await embeddingGenerator.GenerateAsync(chunkTexts, cancellationToken: args.CancellationToken);

                await db.Chunks.Where(c => c.DocumentId == document.Id)
                    .ExecuteDeleteAsync(args.CancellationToken);

                for (var i = 0; i < chunkTexts.Count; i++)
                {
                    db.Chunks.Add(new DocumentChunk
                    {
                        Id = Guid.CreateVersion7(),
                        DocumentId = document.Id,
                        Index = i,
                        Text = chunkTexts[i],
                        Embedding = new Vector(embeddings[i].Vector),
                    });
                }

                logger.LogInformation("Embedded {ChunkCount} chunks for document {DocumentId}",
                    chunkTexts.Count, document.Id);
            }
            else
            {
                logger.LogWarning("No text could be extracted from {FileName} ({ContentType})",
                    document.FileName, document.ContentType);
            }

            document.Status = DocumentStatus.Processed;
            await db.SaveChangesAsync(args.CancellationToken);

            // Only now is the message removed from the queue. If we crashed before
            // this line, the lock would expire and the message would be redelivered.
            await args.CompleteMessageAsync(args.Message);

            logger.LogInformation("Document {DocumentId} processed", message.DocumentId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Processing failed for document {DocumentId} (attempt {Attempt})",
                message.DocumentId, args.Message.DeliveryCount);

            if (args.Message.DeliveryCount >= MaxAttempts)
            {
                // Give up: record the failure and park the message for inspection.
                document.Status = DocumentStatus.Failed;
                await db.SaveChangesAsync(CancellationToken.None);
                await args.DeadLetterMessageAsync(args.Message, "ProcessingFailed", ex.Message);
            }
            else
            {
                // Release the lock immediately so the message is redelivered and retried.
                await args.AbandonMessageAsync(args.Message);
            }
        }
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(args.Exception, "Service Bus error (source: {ErrorSource})", args.ErrorSource);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
