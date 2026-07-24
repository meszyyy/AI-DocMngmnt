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

var server = builder.AddProject<Projects.AiDocMngmnt_Server>("server")
    .WithReference(docdb)
    .WaitFor(docdb)
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(documentBlobs)
    .WaitFor(documentBlobs)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var webfrontend = builder.AddViteApp("webfrontend", "../frontend")
    .WithReference(server)
    .WaitFor(server);

server.PublishWithContainerFiles(webfrontend, "wwwroot");

builder.Build().Run();
