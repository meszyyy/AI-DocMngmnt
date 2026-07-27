using AiDocMngmnt.Data;
using AiDocMngmnt.Worker;
using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

// Same shared defaults as the API: OpenTelemetry, health checks, resilience.
builder.AddServiceDefaults();

// Aspire client integrations — the names match the AppHost resource names.
// UseVector teaches the Npgsql driver and EF about the pgvector column type.
builder.AddNpgsqlDbContext<AppDbContext>("docdb", configureDbContextOptions: options =>
    options.UseNpgsql(npgsql => npgsql.UseVector()));
builder.AddAzureServiceBusClient("messaging");
builder.AddAzureBlobContainerClient("documents", settings =>
{
    if (builder.Environment.IsDevelopment())
    {
        // Pin auth to the Azure CLI login (personal account), same as the API.
        settings.Credential = new AzureCliCredential();
    }
});

// GitHub Models (OpenAI-compatible) behind the Microsoft.Extensions.AI abstractions.
builder.AddAzureChatCompletionsClient("chat")
    .AddChatClient();
builder.AddAzureEmbeddingsClient("embedding")
    .AddEmbeddingGenerator();

builder.Services.AddSingleton<DocumentAnalyzer>();
builder.Services.AddHostedService<DocumentProcessor>();

var host = builder.Build();
host.Run();
