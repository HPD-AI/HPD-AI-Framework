using HPD.Gateway;
using HPD.Gateway.Admission.Redis;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
IConnectionMultiplexer connection = ConnectionMultiplexer.Connect("localhost:6379,abortConnect=false");
builder.Services.AddKeyedSingleton("gateway-admission", connection);
builder.Services.AddHpdGateway(gateway => gateway
    .EnableCoreDeclarations()
    .AddTrafficAdmission(admission =>
    {
        admission.UseRedis("deployment", redis =>
        {
            redis.AuthorityId = "production";
            redis.ConnectionKey = "gateway-admission";
        });
        admission.AddSharedFixedWindow("per-user", "deployment");
    }));
var app = builder.Build();
app.MapHpdGateway();
