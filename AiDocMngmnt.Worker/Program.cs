using AiDocMngmnt.Data;
using AiDocMngmnt.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Same shared defaults as the API: OpenTelemetry, health checks, resilience.
builder.AddServiceDefaults();

// Aspire client integrations — the names match the AppHost resource names.
builder.AddNpgsqlDbContext<AppDbContext>("docdb");
builder.AddAzureServiceBusClient("messaging");

builder.Services.AddHostedService<DocumentProcessor>();

var host = builder.Build();
host.Run();
