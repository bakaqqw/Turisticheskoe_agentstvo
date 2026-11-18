using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using TravelAgency.Data;
using TravelAgency.Endpoints;
using TravelAgency.Models;
using TravelAgency.Services;

var passwordCommand = args.Length > 0 && args[0] == "--change-password";
var builder = WebApplication.CreateBuilder(passwordCommand ? [] : args);
var dataDirectory = Environment.GetEnvironmentVariable("TRAVEL_DATA_DIR")
    ?? Path.Combine(builder.Environment.ContentRootPath, "DataFiles");
dataDirectory = Path.GetFullPath(dataDirectory);
Directory.CreateDirectory(dataDirectory);
builder.Services.AddSingleton(new AgencyDatabase(Path.Combine(dataDirectory, "travel_agency.sqlite3")));
builder.Services.AddSingleton<PasswordHasher<Employee>>();
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory,"keys")))
    .SetApplicationName("TravelAgency");
builder.Services.AddAntiforgery(options => {
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "TravelAgency.Csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options => {
    options.Cookie.Name = "TravelAgency.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context => {context.Response.StatusCode = 401; return Task.CompletedTask;};
    options.Events.OnRedirectToAccessDenied = context => {context.Response.StatusCode = 403; return Task.CompletedTask;};
    options.Events.OnValidatePrincipal = async context => {
        var idText = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var db = context.HttpContext.RequestServices.GetRequiredService<AgencyDatabase>();
        var employee = long.TryParse(idText, out var id) ? db.FindEmployee(id:id) : null;
        if (employee is null || !employee.IsActive || employee.Role != context.Principal?.FindFirstValue(ClaimTypes.Role)
            || AccountPasswordService.Fingerprint(employee.PasswordHash) != context.Principal?.FindFirstValue(AccountPasswordService.FingerprintClaim)) {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync();
        }
    };
});
builder.Services.AddAuthorization();
var app = builder.Build();
if (passwordCommand) {
    var database=app.Services.GetRequiredService<AgencyDatabase>();
    if (File.Exists(database.FilePath)) DatabaseInitializer.Initialize(database);
    Environment.ExitCode = AccountPasswordConsole.Run(args, app.Services.GetRequiredService<AgencyDatabase>());
    return;
}
DatabaseInitializer.Initialize(app.Services.GetRequiredService<AgencyDatabase>());
AccountPasswordService.ApplyPendingPasswords(app.Services.GetRequiredService<AgencyDatabase>());
if (args.Contains("--init-db")) { Console.WriteLine("База SQLite создана и проверена."); return; }

app.Use(async (context, next) => {
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    try { await next(); }
    catch (SqliteException error) {
        app.Logger.LogError(error,"Ошибка доступа к SQLite");
        context.Response.StatusCode = error.SqliteErrorCode is 5 or 6 ? 409 : 400;
        await context.Response.WriteAsJsonAsync(new {message="Не удалось сохранить данные. Проверьте значения и повторите попытку."});
    }
    catch (Exception error) {
        app.Logger.LogError(error,"Необработанная ошибка");
        context.Response.StatusCode=500;
        await context.Response.WriteAsJsonAsync(new {message="Не удалось выполнить операцию. Повторите попытку."});
    }
});
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache" });
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context,next) => {
    if (context.Request.Path.StartsWithSegments("/api") &&
        !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)) {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) {
            context.Response.StatusCode=400;
            await context.Response.WriteAsJsonAsync(new {message="Срок действия формы истёк. Обновите страницу и повторите отправку."});
            return;
        }
    }
    await next();
});
app.MapAuthEndpoints();
app.MapAgencyEndpoints();
app.MapMethods("/api/{**path}",["GET","POST","PUT","PATCH","DELETE"],() => Results.NotFound(new {message="Операция отсутствует в текущей версии."}));
Console.WriteLine("Информационная система для туристического агентства. Готовность 50%. База — SQLite.");
if (string.IsNullOrEmpty(builder.Configuration["urls"])) {
    var selected = 0;
    for (var port=5080;port<=5090;port++) {
        try {
            var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,port);
            listener.Start();listener.Stop();selected=port;break;
        } catch (System.Net.Sockets.SocketException) { }
    }
    if (selected==0) throw new InvalidOperationException("Порты 5080–5090 заняты. Укажите свободный порт параметром --urls.");
    app.Urls.Add($"http://127.0.0.1:{selected}");
}
app.Lifetime.ApplicationStarted.Register(() => {
    var address=app.Urls.First();
    Console.WriteLine($"Откройте сайт: {address}. Остановка: Ctrl+C.");
    if (Environment.GetEnvironmentVariable("TRAVEL_OPEN_BROWSER")=="1") {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(address) {UseShellExecute=true}); }
        catch { Console.WriteLine("Откройте указанный адрес вручную в браузере."); }
    }
});
app.Run();
