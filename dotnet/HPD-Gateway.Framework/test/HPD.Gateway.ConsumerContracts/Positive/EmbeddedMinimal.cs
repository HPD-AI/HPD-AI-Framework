using HPD.Gateway;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHpdGateway(_ => { });
var app = builder.Build();
app.MapHpdGateway();
