var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .AddDatabase("Default");

var redis = builder.AddRedis("redis")
    .WithLifetime(ContainerLifetime.Persistent);

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator();
var blobs = storage.AddBlobs("blobs");

var livekit = builder.AddContainer("livekit", "livekit/livekit-server", "v1.10.1")
    .WithArgs("--dev", "--bind", "0.0.0.0")
    .WithEndpoint("signal", e => { e.Port = 7880; e.TargetPort = 7880; e.Transport = "http"; e.IsProxied = false; })
    .WithLifetime(ContainerLifetime.Persistent);

var api = builder.AddProject<Projects.Codec_Api>("api")
    .WithEndpoint("http", e => { e.Port = 5050; e.IsProxied = false; })
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(blobs)
    .WaitFor(postgres)
    .WaitFor(redis)
    .WaitFor(storage)
    .WaitFor(livekit)
    .WithHttpHealthCheck("/health/ready");

var web = builder.AddViteApp("web", "../../web")
    .WithEndpoint("http", e => { e.Port = 5174; e.IsProxied = false; })
    .WithReference(api)
    .WaitFor(api);

var admin = builder.AddViteApp("admin", "../../admin")
    .WithEndpoint("http", e => { e.Port = 5175; e.IsProxied = false; })
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
