using HPD.Payments.Host.Api;

var selfTest = args.Contains("--self-test", StringComparer.Ordinal);
var selectsInMemory = args.Contains("--inmemory", StringComparer.Ordinal);
var selectsSqlite = args.Contains("--sqlite", StringComparer.Ordinal);
if (!selfTest && selectsInMemory == selectsSqlite)
{
    await Console.Error.WriteLineAsync("HPD.Payments Host.Api requires exactly one of --inmemory or --sqlite.").ConfigureAwait(false);
    return 64;
}
var profile = selectsSqlite ? PaymentsApiProfile.EmbeddedSqlite : PaymentsApiProfile.EmbeddedInMemory;
var configuration = new PaymentsApiConfiguration(profile);
if (selfTest)
{
    PaymentsApiTransport.RequireVersion(configuration.WireVersion);
    Console.WriteLine($"PASS Host.Api {configuration.WireVersion} profile={configuration.Profile} routes={PaymentsApiTransport.Routes.Count}");
    return 0;
}

var builder = WebApplication.CreateSlimBuilder(args);
var app = builder.Build();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments(new PathString("/hpd/payments/v1"), StringComparison.Ordinal))
    {
        context.Response.Headers["x-hpd-payments-profile"] = configuration.Profile.ToString();
        if (!StringComparer.Ordinal.Equals(context.Request.Headers["x-hpd-payments-version"], configuration.WireVersion))
        {
            context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"payments.api.versionUnsupported\",\"version\":\"hpd.payments.api.v1\"}").ConfigureAwait(false);
            return;
        }
    }
    await next(context).ConfigureAwait(false);
});
app.MapGet("/hpd/payments/v1/health", () => TypedResults.Text("{\"status\":\"ready\",\"version\":\"hpd.payments.api.v1\"}", "application/json"));
app.MapGet("/hpd/payments/v1/manifest", () => TypedResults.Text("{\"version\":\"hpd.payments.api.v1\",\"authorityLogic\":false}", "application/json"));
await app.RunAsync().ConfigureAwait(false);
return 0;
