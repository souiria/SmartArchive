var builder = DistributedApplication.CreateBuilder(args);
builder.AddProject<Projects.SmartArchive_Web>("smartarchive-web").WithExternalHttpEndpoints();
builder.Build().Run();
