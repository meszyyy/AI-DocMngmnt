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

// Azure OpenAI account with two model deployments — provisioned into the
// subscription automatically, just like the storage account.
// (Replaces GitHub Models, which was retired on 2026-07-30.)
// GlobalStandard SKU routes globally, so model availability does not depend
// on the resource group's region.
var openai = builder.AddAzureOpenAI("openai");

// gpt-4o-mini is already deprecating (no new deployments); gpt-5.4-mini is
// the current small model with the longest support runway. DataZoneStandard:
// the subscription has quota for it here (GlobalStandard had none), and it
// keeps processing within the EU data zone.
openai.AddDeployment("chat", "gpt-5.4-mini", "2026-03-17")
    .WithProperties(d => d.SkuName = "DataZoneStandard");

// Embedding model for semantic search (1536-dimension vectors).
openai.AddDeployment("embedding", "text-embedding-3-small", "1")
    .WithProperties(d => d.SkuName = "GlobalStandard");

var server = builder.AddProject<Projects.AiDocMngmnt_Server>("server")
    .WithReference(docdb)
    .WaitFor(docdb)
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(documentBlobs)
    .WaitFor(documentBlobs)
    .WithReference(serviceBus)
    .WaitFor(documentsQueue)
    .WithReference(openai)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

// The worker consumes the queue, downloads the blob, runs the AI analysis
// and updates the document in the database.
builder.AddProject<Projects.AiDocMngmnt_Worker>("worker")
    .WithReference(docdb)
    .WaitFor(docdb)
    .WithReference(serviceBus)
    .WaitFor(documentsQueue)
    .WithReference(documentBlobs)
    .WaitFor(documentBlobs)
    .WithReference(openai);

var webfrontend = builder.AddViteApp("webfrontend", "../frontend")
    .WithReference(server)
    .WaitFor(server);

server.PublishWithContainerFiles(webfrontend, "wwwroot");

builder.Build().Run();
