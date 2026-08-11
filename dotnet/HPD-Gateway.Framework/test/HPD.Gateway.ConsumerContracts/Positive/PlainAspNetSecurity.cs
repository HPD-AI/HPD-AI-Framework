using HPD.Gateway;
using HPD.Gateway.ControlPlane;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthentication("gateway-admin-cookie").AddCookie("gateway-admin-cookie");
builder.Services.AddAuthorization(options => options.AddPolicy(
    "gateway-admin",
    policy => policy.RequireAuthenticatedUser()));
builder.Services.AddHpdGateway(gateway => gateway.EnableCoreDeclarations());
builder.Services.AddHpdGatewayControlPlane(controlPlane => controlPlane
    .UseProcessLocalAuthority()
    .AddAdminApi(options =>
    {
        options.AuthenticationScheme = "gateway-admin-cookie";
        options.AuthorizationPolicy = "gateway-admin";
    }));
var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapHpdGateway();
app.MapHpdGatewayControlPlane();
