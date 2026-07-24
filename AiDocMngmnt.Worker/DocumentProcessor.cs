using System.Text.Json;
using AiDocMngmnt.Data;
using Azure.Messaging.ServiceBus;

namespace AiDocMngmnt.Worker;

public class DocumentProcessor(
    ServiceBusClient serviceBusClient,
    IServiceScopeFactory scopeFactory,
    ILogger<DocumentProcessor> logger) : BackgroundService
{
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

        document.Status = DocumentStatus.Processing;
        await db.SaveChangesAsync(args.CancellationToken);

        // Placeholder for the real work — Phase 5 replaces this with
        // text extraction, summarization and tagging.
        await Task.Delay(TimeSpan.FromSeconds(5), args.CancellationToken);

        document.Status = DocumentStatus.Processed;
        await db.SaveChangesAsync(args.CancellationToken);

        // Only now is the message removed from the queue. If we crashed before
        // this line, the lock would expire and the message would be redelivered.
        await args.CompleteMessageAsync(args.Message);

        logger.LogInformation("Document {DocumentId} processed", message.DocumentId);
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
