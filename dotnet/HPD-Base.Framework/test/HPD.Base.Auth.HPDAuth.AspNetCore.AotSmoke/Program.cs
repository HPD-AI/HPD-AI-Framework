using System.Security.Claims;
using HPD.Base.AspNetCore.Http;
using HPD.Base.Auth.HPDAuth.AspNetCore.DependencyInjection;
using HPD.Base.Policy;
using HPD.Base.Runtime;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddAuthorization(options => options.AddHPDBaseHPDAuthAdminPolicy());
builder.Services.AddHPDBaseHPDAuthAspNetCore();

var app = builder.Build();
var mapper = app.Services.GetRequiredService<IBaseHttpPrincipalMapper>();
var scope = app.Services.CreateScope();
var context = new DefaultHttpContext
{
    RequestServices = scope.ServiceProvider,
    User = new ClaimsPrincipal(new ClaimsIdentity(
    [
        new Claim("sub", "admin-1"),
        new Claim("role", "Admin"),
        new Claim("instance_id", "tenant-1")
    ], "HPD"))
};

var principal = await mapper.TryMapAsync(context);
Require(principal?.AuthenticationState == PrincipalAuthenticationState.Admin, "Admin principal was not mapped.");
Require(principal!.Subjects?.Any(subject => subject.Kind == AccessSubjectKind.Admin) == true, "Admin subject was not mapped.");

scope.Dispose();

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
