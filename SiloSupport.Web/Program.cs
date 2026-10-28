using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SiloSupport.Application.Services;
using SiloSupport.Persistence;
using SiloSupport.Web.Components;
using SiloSupport.Web.Security;

var builder = WebApplication.CreateBuilder(args);

// 1. DbContext, repositories and unit of work (SQL Server). The Web project owns and applies the migrations.
builder.Services.AddSupportPersistence(builder.Configuration["Database:ConnectionString"]);

// 2. Identity + cookie authentication.
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.LogoutPath = "/Account/Logout";
    o.AccessDeniedPath = "/Account/AccessDenied";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromDays(7);
});

// 3. Application services used by the pages. Receiving Bale messages is NOT done here: that is the job of the
//    SiloSupport.Service.CustomerSupport Windows service.
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<CustomerMemberService>();
builder.Services.AddScoped<CustomerGroupService>();
builder.Services.AddScoped<ConversationService>();

// 4. Blazor + Telerik UI for Blazor.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();
builder.Services.AddTelerikBlazor();

var app = builder.Build();

// 5. Migrate, seed roles and administrator.
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    await IdentitySeeder.SeedAsync(scope.ServiceProvider, app.Configuration, logger);
}

// 6. Middleware.
if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// 7. Login / logout are plain HTTP endpoints: Blazor Server circuits cannot set cookies over SignalR.
app.MapPost("/Account/LoginPost", async (
    [FromForm] string? username,
    [FromForm] string? password,
    [FromForm] string? returnUrl,
    SignInManager<ApplicationUser> signInManager) =>
{
    var safeReturnUrl = ReturnUrlHelper.Resolve(returnUrl);

    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        return Results.Redirect(BuildLoginUrl(safeReturnUrl, "Username and password are required."));

    var result = await signInManager.PasswordSignInAsync(username, password, isPersistent: false, lockoutOnFailure: true);
    return result.Succeeded
        ? Results.LocalRedirect(safeReturnUrl)
        : Results.Redirect(BuildLoginUrl(safeReturnUrl, result.IsLockedOut
            ? "Account temporarily locked. Try again later."
            : "Invalid username or password."));
}).AllowAnonymous();

app.MapGet("/Account/LogoutPost", async (HttpContext http, SignInManager<ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    http.Response.Headers.CacheControl = "no-store";
    return Results.Redirect("/Account/Login");
}).AllowAnonymous();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

static string BuildLoginUrl(string returnUrl, string error) =>
    $"/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}&error={Uri.EscapeDataString(error)}";

/// <summary>Enables <c>WebApplicationFactory&lt;Program&gt;</c> in tests.</summary>
public partial class Program { }
