using HPD.Gateway;
using HPD.Gateway.ControlPlane;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHpdGateway(gateway => gateway.EnableCoreDeclarations());
builder.Services.AddHpdGatewayControlPlane(controlPlane => controlPlane
    .UseProcessLocalAuthority()
    .AddAdminApi()
    .AddStudio());
var app = builder.Build();
app.MapHpdGateway();
app.MapHpdGatewayControlPlane();
