using HPD.Gateway;
using HPD.Gateway.ControlPlane;
using HPD.Gateway.ControlPlane.Sqlite;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHpdGateway(gateway => gateway.EnableCoreDeclarations());
builder.Services.AddHpdGatewayControlPlane(controlPlane => controlPlane
    .UseSqlite(options =>
    {
        options.DataSource = "gateway.db";
        options.PlanProtectionKey = Enumerable.Repeat((byte)0x11, 32).ToArray();
        options.TokenProtectionKey = Enumerable.Repeat((byte)0x22, 32).ToArray();
        options.DesiredStateTokenKey = Enumerable.Repeat((byte)0x33, 32).ToArray();
        options.EpochReservationKey = Enumerable.Repeat((byte)0x44, 32).ToArray();
    })
    .AddAdminApi());
var app = builder.Build();
app.MapHpdGateway();
app.MapHpdGatewayControlPlane();
