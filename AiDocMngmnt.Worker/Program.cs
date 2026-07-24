using AiDocMngmnt.Data;
using AiDocMngmnt.Worker;
using Azure.Identity;

var builder = Host.CreateApplicationBuilder(args);

// Same shared defaults as the API: OpenTelemetry, health checks, resilience.
builder.AddServiceDefaults();

// Aspire client integrations — the names match the AppHost resource names.
builder.AddNpgsqlDbContext<AppDbContext>("docdb");
builder.AddAzureServiceBusClient("messaging");
builder.AddAzureBlobContainerClient("documents", settings =>
{
    if (builder.Environment.IsDevelopment())
    {
        // Pin auth to the Azure CLI login (personal account), same as the API.
        settings.Credential = new AzureCliCredential();
    }
});

// GitHub Models (OpenAI-compatible) chat client behind the IChatClient abstraction.
builder.AddAzureChatCompletionsClient("chat")
    .AddChatClient();

builder.Services.AddSingleton<DocumentAnalyzer>();
builder.Services.AddHostedService<DocumentProcessor>();

var host = builder.Build();
host.Run();
