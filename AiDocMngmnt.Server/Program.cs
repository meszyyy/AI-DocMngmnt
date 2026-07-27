using System.Text.Json.Serialization;
using AiDocMngmnt.Data;
using AiDocMngmnt.Server;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Serialize enums as text ("Uploaded") in JSON instead of numbers (0).
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Aspire client integrations: the names ("docdb", "cache") match the AppHost
// resource names — connection strings are injected by the AppHost.
// UseVector teaches the Npgsql driver and EF about the pgvector column type.
builder.AddNpgsqlDbContext<AppDbContext>("docdb", configureDbContextOptions: options =>
    options.UseNpgsql(npgsql => npgsql.UseVector()));
builder.AddRedisOutputCache("cache");

// Embedding generator for turning search queries into vectors.
builder.AddAzureEmbeddingsClient("embedding")
    .AddEmbeddingGenerator();
builder.AddAzureBlobContainerClient("documents", settings =>
{
    if (builder.Environment.IsDevelopment())
    {
        // Pin auth to the Azure CLI login (personal account). The default
        // credential chain could pick up a different signed-in account
        // (e.g. Visual Studio) on this machine.
        settings.Credential = new AzureCliCredential();
    }
});
builder.AddAzureServiceBusClient("messaging");

// Senders are thread-safe and meant to be reused — register one as a singleton.
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<ServiceBusClient>().CreateSender(Queues.DocumentsToProcess));

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();
app.UseOutputCache();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Apply pending EF migrations on startup in development only.
    // In production this would be a dedicated deployment step
    // (multiple instances would race on it).
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

var api = app.MapGroup("/api");
api.MapDocumentEndpoints();

app.MapDefaultEndpoints();

app.UseFileServer();

app.Run();
