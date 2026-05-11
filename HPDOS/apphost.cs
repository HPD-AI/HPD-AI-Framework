#:sdk Aspire.AppHost.Sdk@13.2.0
#:package Aspire.Hosting.JavaScript@13.2.0
#:property NoWarn=ASPIRECSHARPAPPS001

var builder = DistributedApplication.CreateBuilder(args);

var backend = builder.AddCSharpApp("backend", "backend/backend.cs")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
    .WithExternalHttpEndpoints();

var frontend = builder.AddViteApp("frontend", "frontend")
    .WithBun(install: false)
    .WithExternalHttpEndpoints();

builder.Build().Run();
