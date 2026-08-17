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

// Azure OpenAI client with the two deployments behind the
// Microsoft.Extensions.AI abstractions (analysis chat + chunk embeddings).
var azureOpenAI = builder.AddAzureOpenAIClient("openai", settings =>
{
    if (builder.Environment.IsDevelopment())
    {
        // Pin auth to the Azure CLI login, same as for blob storage.
        settings.Credential = new AzureCliCredential();
    }
});
azureOpenAI.AddChatClient("chat");
azureOpenAI.AddEmbeddingGenerator("embedding");

builder.Services.AddSingleton<DocumentAnalyzer>();
builder.Services.AddHostedService<DocumentProcessor>();

var host = builder.Build();
host.Run();
