using HPD.Gateway;
using HPD.Gateway.ControlPlane;
using HPD.Gateway.ControlPlane.HPDAuth;
using HPD.Gateway.Discovery.Microsoft;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHpdGateway(gateway => gateway
    .EnableCoreDeclarations()
    .AddMicrosoftDiscovery("aspire", discovery => discovery.AddConfiguration()));
builder.Services.AddHpdGatewayControlPlane(controlPlane => controlPlane
    .UseProcessLocalAuthority()
    .AddAdminApi()
    .AddStudio()
    .AddHpdAuth("hpd-cloud-gateway"));
var app = builder.Build();
app.MapHpdGateway();
app.MapHpdGatewayControlPlane();
