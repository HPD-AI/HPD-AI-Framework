using System.Net;
using HPD.Gateway;
using HPD.Gateway.Hosting;
using HPD.Gateway.Inspection;
using HPD.Gateway.OutputCaching;
using HPD.Gateway.Resilience;
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
    gateway.AddRequestInspection(
        inspectors => inspectors.Add("standalone-unencoded", new StandaloneUnencodedInspector()));
    gateway.ProtectCredentialHeaders("x-api-key");
    gateway.AddUpstreamResilience(profiles => profiles.Add(new GatewayResilienceProfile
    {
        Name = "standalone-safe",
        Version = 1,
        Retry = new GatewayResponseRetryProfile
        {
            StatusCodes = [HttpStatusCode.ServiceUnavailable],
            MaximumRetryAttempts = 1
        }
    }));
    gateway.AddOutputCaching(profiles => profiles.Add(new GatewayOutputCacheProfile
    {
        Name = "standalone-cache",
        Version = 1,
        Expiration = TimeSpan.FromMinutes(1)
    }));
    gateway.UseInitialCandidate(inputs.InitialCandidate);
});

await using var application = builder.Build();
application.MapHpdGateway();
await application.RunAsync();

internal sealed class StandaloneUnencodedInspector : IGatewayRequestInspector
{
    public ValueTask<GatewayInspectionDecision> InspectAsync(
        GatewayInspectionContext context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(context.ContentEncoded
            ? GatewayInspectionDecision.Reject("encoded-body-unsupported", 415)
            : GatewayInspectionDecision.Allow());
}
