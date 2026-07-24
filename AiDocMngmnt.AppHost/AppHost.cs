var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL with the pgvector extension (needed later for semantic search).
// WithDataVolume: data survives container replacement.
// Persistent lifetime: the container keeps running after the apphost stops,
// so the next `aspire run` starts in seconds.
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg17")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithPgAdmin();

var docdb = postgres.AddDatabase("docdb");

var cache = builder.AddRedis("cache")
    .WithLifetime(ContainerLifetime.Persistent);

// Real Azure Blob Storage: on first run Aspire provisions the resource group,
// the storage account and the RBAC role assignment automatically, using the
// subscription configured in user secrets (Azure:SubscriptionId etc.).
var storage = builder.AddAzureStorage("storage");

// Modeling the container as a resource means Aspire creates it on startup
// and can inject a ready-to-use BlobContainerClient into the server.
var documentBlobs = storage.AddBlobContainer("documents");

// Azure Service Bus running as a local emulator container. The queue resource
// is materialized into the emulator's configuration on startup.
var serviceBus = builder.AddAzureServiceBus("messaging")
    .RunAsEmulator();

var documentsQueue = serviceBus.AddServiceBusQueue(name: "documents-to-process");

var server = builder.AddProject<Projects.AiDocMngmnt_Server>("server")
    .WithReference(docdb)
    .WaitFor(docdb)
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(documentBlobs)
    .WaitFor(documentBlobs)
    .WithReference(serviceBus)
    .WaitFor(documentsQueue)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

// The worker consumes the queue and updates document status in the database.
builder.AddProject<Projects.AiDocMngmnt_Worker>("worker")
    .WithReference(docdb)
    .WaitFor(docdb)
    .WithReference(serviceBus)
    .WaitFor(documentsQueue);

var webfrontend = builder.AddViteApp("webfrontend", "../frontend")
    .WithReference(server)
    .WaitFor(server);

server.PublishWithContainerFiles(webfrontend, "wwwroot");

builder.Build().Run();
