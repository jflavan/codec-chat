var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .AddDatabase("Default");

var redis = builder.AddRedis("redis");

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator();
var blobs = storage.AddBlobs("blobs");

var api = builder.AddProject<Projects.Codec_Api>("api")
    .WithEndpoint("http", e => { e.Port = 5050; e.IsProxied = false; })
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(blobs)
    .WaitFor(postgres)
    .WaitFor(redis)
    .WithHttpHealthCheck("/health/ready");

var web = builder.AddViteApp("web", "../../web")
    .WithEndpoint("http", e => { e.Port = 5174; e.IsProxied = false; })
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
