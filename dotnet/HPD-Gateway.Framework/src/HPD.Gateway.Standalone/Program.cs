using HPD.Gateway;
using HPD.Gateway.Hosting;
using HPD.Gateway.Standalone;

if (args.Length != 1)
    throw new InvalidOperationException("Usage: HPD.Gateway.Standalone <absolute-bootstrap-json-path>");

var inputs = GatewayStandaloneBootstrapReader.Read(args[0]);
var builder = WebApplication.CreateSlimBuilder(args);
builder.UseHpdGatewayHost(inputs.Host, certificates =>
{
    foreach (var (reference, source) in inputs.Certificates)
        certificates.Add(reference, source);
});
builder.Services.AddHpdGateway(gateway =>
{
    gateway.AddCoreFamilies();
    gateway.UseInitialCandidate(inputs.InitialCandidate);
});

await using var application = builder.Build();
application.MapHpdGateway();
await application.RunAsync();
