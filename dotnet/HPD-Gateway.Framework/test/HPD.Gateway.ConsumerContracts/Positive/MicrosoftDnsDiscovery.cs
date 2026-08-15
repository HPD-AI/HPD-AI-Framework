using HPD.Gateway;
using HPD.Gateway.Discovery.Microsoft;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHpdGateway(gateway => gateway
    .EnableCoreDeclarations()
    .AddMicrosoftDiscovery("dns", discovery => discovery
        .AddDns()
        .AddDnsSrv(new GatewayMicrosoftDnsSrvOptions("svc.cluster.local"))));
var app = builder.Build();
app.MapHpdGateway();
