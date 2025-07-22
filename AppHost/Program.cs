var builder = DistributedApplication.CreateBuilder(args);

var mongo = builder.AddMongoDB("default").WithMongoExpress().WithLifetime(ContainerLifetime.Persistent);

builder.AddProject<Projects.URLShortener>("URLShortener")
    .WithReference(mongo)
    .WaitFor(mongo)
    .WithReplicas(1);

builder.Build().Run();