using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using SmartArchive.Application;
using SmartArchive.Domain.Entities;
using SmartArchive.Infrastructure;
using SmartArchive.Infrastructure.Persistence;
using SmartArchive.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
builder.Services.AddLocalization();
builder.Services.AddControllers();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/login";
});

var app = builder.Build();

// Apply EF Core Migrations & Seed Enterprise Data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<ApplicationDbContext>();
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();

    await context.Database.MigrateAsync();
    await ApplicationDbContextSeed.SeedAsync(context, userManager, roleManager);
}

var supportedCultures = new[] { new CultureInfo("fr-FR"), new CultureInfo("ar-MA") };
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("fr-FR"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Minimal Endpoints for Login, Logout & Culture Switching
app.MapPost("/account/login", async (
    [FromForm] string email,
    [FromForm] string password,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager) =>
{
    var user = await userManager.FindByEmailAsync(email);
    if (user is null || !user.IsActive) return Results.Redirect("/login?error=1");

    var result = await signInManager.PasswordSignInAsync(user, password, isPersistent: true, lockoutOnFailure: true);
    if (!result.Succeeded) return Results.Redirect("/login?error=1");

    if (user.DepartmentId.HasValue)
    {
        await signInManager.SignInWithClaimsAsync(user, isPersistent: true, new[]
        {
            new Claim("DepartmentId", user.DepartmentId.Value.ToString())
        });
    }

    return Results.Redirect("/");
}).DisableAntiforgery();

app.MapPost("/account/logout", async (SignInManager<ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Redirect("/login");
}).DisableAntiforgery();

app.MapGet("/culture/set", (string culture, string redirectUri, HttpContext httpContext) =>
{
    httpContext.Response.Cookies.Append(
        CookieRequestCultureProvider.DefaultCookieName,
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
        new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1) });
    return Results.LocalRedirect(redirectUri);
});

app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
