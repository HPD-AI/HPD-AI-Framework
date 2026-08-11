using HPD.Gateway;
using HPD.Gateway.Discovery.Microsoft;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHpdGateway(gateway => gateway
    .EnableCoreDeclarations()
    .AddMicrosoftDiscovery("aspire", discovery => discovery.AddConfiguration()));
var app = builder.Build();
app.MapHpdGateway();
