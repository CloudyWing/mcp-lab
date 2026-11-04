using System.Security.Claims;
using CloudyWing.McpLab.Admin.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using MudBlazor.Services;

string port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
string configRoot = Environment.GetEnvironmentVariable("MCPLAB_CONFIG_ROOT") ?? "/app/config";
string serviceUrlTemplate = Environment.GetEnvironmentVariable("MCPLAB_SERVICE_URL_TEMPLATE") ?? "http://{0}:8080";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

ConfigStore store = new(configRoot);
Directory.CreateDirectory(Path.Combine(store.AdminDirectory, "keys"));
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(store.AdminDirectory, "keys")))
    .SetApplicationName("mcp-lab-admin");

builder.Services.AddSingleton(store);
builder.Services.AddSingleton(new ServiceEndpoints(serviceUrlTemplate));
builder.Services.AddSingleton(new AdminPassword(Environment.GetEnvironmentVariable(AdminPassword.VariableName)));
builder.Services.AddHttpClient<ServiceStatusClient>();
builder.Services.AddSingleton<ConnectionTester>();
builder.Services.AddScoped<ConfigApplier>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => {
        options.LoginPath = "/login";
        options.Cookie.Name = "mcplab-admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();

WebApplication app = builder.Build();
app.MapStaticAssets();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/health", () => Results.Json(new { status = "ok", service = "mcp-admin" }));

app.MapPost("/account/login", async (HttpContext context, AdminPassword password, [FromForm] string? secret, [FromForm] string? returnUrl) => {
    if (!password.Verify(secret)) {
        // Slows down guessing without locking out the only account.
        await Task.Delay(TimeSpan.FromSeconds(1));
        return Results.Redirect("/login?failed=1");
    }

    ClaimsIdentity identity = new([new Claim(ClaimTypes.Name, "admin")], CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

    return Results.LocalRedirect(string.IsNullOrEmpty(returnUrl) || !returnUrl.StartsWith('/') || returnUrl.StartsWith("//") ? "/" : returnUrl);
});

app.MapPost("/account/logout", async (HttpContext context) => {
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).RequireAuthorization();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
await app.RunAsync();
