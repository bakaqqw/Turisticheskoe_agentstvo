using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using TravelAgency.Data;
using TravelAgency.Models;
using TravelAgency.Services;

namespace TravelAgency.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/api/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new {token=antiforgery.GetAndStoreTokens(context).RequestToken}));
        app.MapPost("/api/auth/login", async (LoginInput input, HttpContext context,
            AgencyDatabase db, PasswordHasher<Employee> hasher) => {
            var employee=db.FindEmployee(username:input.Username?.Trim());
            // A dummy hash also makes nonexistent users perform a password verification.
            var candidate=employee ?? new Employee(0,"","","manager",DummyHash,false);
            var result=hasher.VerifyHashedPassword(candidate,candidate.PasswordHash,input.Password ?? "");
            if (employee is null || !employee.IsActive || result==PasswordVerificationResult.Failed)
                return Results.Json(new {message="Неверный логин или пароль."},statusCode:401);
            var claims=new[] {new Claim(ClaimTypes.NameIdentifier,employee.Id.ToString()),
                new Claim(ClaimTypes.Name,employee.FullName),new Claim(ClaimTypes.Role,employee.Role),
                new Claim(AccountPasswordService.FingerprintClaim,AccountPasswordService.Fingerprint(employee.PasswordHash))};
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims,CookieAuthenticationDefaults.AuthenticationScheme)),
                new AuthenticationProperties {IsPersistent=false});
            return Results.Ok(new {employee.Id,employee.Username,employee.FullName,employee.Role});
        });
        app.MapGet("/api/auth/me",(HttpContext context,AgencyDatabase db) => {
            var employee=db.FindEmployee(id:long.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!))!;
            return Results.Ok(new {employee.Id,employee.Username,employee.FullName,employee.Role});
        }).RequireAuthorization();
        app.MapPost("/api/auth/logout", async (HttpContext context) => {
            await context.SignOutAsync(); return Results.NoContent();
        }).RequireAuthorization();
    }
    private static readonly string DummyHash = new PasswordHasher<Employee>().HashPassword(
        new Employee(0,"","","manager","",false),"DummyVerificationPassword!2027");
}
