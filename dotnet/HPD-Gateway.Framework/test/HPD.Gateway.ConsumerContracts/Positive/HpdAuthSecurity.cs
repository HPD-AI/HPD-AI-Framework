using HPD.Gateway;
using HPD.Gateway.ControlPlane;
using HPD.Gateway.ControlPlane.HPDAuth;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHpdGateway(gateway => gateway.EnableCoreDeclarations());
builder.Services.AddHpdGatewayControlPlane(controlPlane => controlPlane
    .UseProcessLocalAuthority()
    .AddAdminApi()
    .AddHpdAuth("gateway-admin"));
var app = builder.Build();
app.MapHpdGateway();
app.MapHpdGatewayControlPlane();
