using HPD.Gateway;
using HPD.Gateway.Admission.Redis;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHpdGateway(gateway => gateway
    .EnableCoreDeclarations()
    .AddTrafficAdmission(admission =>
    {
        admission.UseRedis("deployment", redis =>
        {
            redis.AuthorityId = "production";
            redis.Configuration = "localhost:6379,abortConnect=false";
        });
        admission.AddSharedFixedWindow("per-user", "deployment");
    }));
var app = builder.Build();
app.MapHpdGateway();
