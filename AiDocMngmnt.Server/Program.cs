using System.Text.Json.Serialization;
using AiDocMngmnt.Server;
using AiDocMngmnt.Server.Data;
using Microsoft.EntityFrameworkCore;

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
builder.AddNpgsqlDbContext<AppDbContext>("docdb");
builder.AddRedisOutputCache("cache");

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
