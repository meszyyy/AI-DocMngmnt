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

var server = builder.AddProject<Projects.AiDocMngmnt_Server>("server")
    .WithReference(docdb)
    .WaitFor(docdb)
    .WithReference(cache)
    .WaitFor(cache)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var webfrontend = builder.AddViteApp("webfrontend", "../frontend")
    .WithReference(server)
    .WaitFor(server);

server.PublishWithContainerFiles(webfrontend, "wwwroot");

builder.Build().Run();
