var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .AddDatabase("codec-dev");

var redis = builder.AddRedis("redis");

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator();
var blobs = storage.AddBlobs("blobs");

var api = builder.AddProject<Projects.Codec_Api>("api")
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(blobs)
    .WaitFor(postgres)
    .WaitFor(redis)
    .WithHttpHealthCheck("/health/ready");

builder.AddViteApp("web", "../../web")
    .WithHttpEndpoint(port: 5174, env: "PORT")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
