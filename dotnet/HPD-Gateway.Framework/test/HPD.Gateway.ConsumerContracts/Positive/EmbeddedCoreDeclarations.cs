using HPD.Gateway;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHpdGateway(gateway => gateway.EnableCoreDeclarations());
var app = builder.Build();
app.MapHpdGateway();
