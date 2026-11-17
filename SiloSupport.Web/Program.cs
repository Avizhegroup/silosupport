using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SiloSupport.Application.Helpers;
using SiloSupport.Application.Services;
using SiloSupport.Domain.Enums;
using SiloSupport.Persistence;
using Microsoft.AspNetCore.Authorization;
using SiloSupport.Web.Components;
using SiloSupport.Web.Localization;
using SiloSupport.Web.Security;
using Telerik.Blazor.Services;
using SiloSupport.Infrastructure.Extensions;

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
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromDays(7);
});

builder.Services.AddSiloSerilog(builder.Configuration);

// 3. Application services used by the pages. Receiving Bale messages is NOT done here: that is the job of the
//    SiloSupport.Service.CustomerSupport Windows service.
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<CustomerMemberService>();
builder.Services.AddScoped<SupportPersonService>();
builder.Services.AddScoped<CustomerGroupService>();
builder.Services.AddScoped<ConversationService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<DashboardService>();

// 4. Blazor + Telerik UI for Blazor.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization(options =>
{
    // Application level: every endpoint requires a signed-in user unless it is explicitly [AllowAnonymous]
    // (the login / access-denied pages and the login and logout endpoints). Role level: the admin pages and the
    // sidebar additionally require the Administrator role.
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});
builder.Services.AddTelerikBlazor();
builder.Services.AddSingleton(typeof(ITelerikStringLocalizer), typeof(TelerikPersianLocalizer));

var app = builder.Build();

// 5. Migrate, seed roles and administrator.
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    await IdentitySeeder.SeedAsync(scope.ServiceProvider, app.Configuration, logger);
}

// 6. Middleware. Support HTTP-only deployments without HTTPS redirection or HSTS.
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
        return Results.Redirect(BuildLoginUrl(safeReturnUrl, "نام کاربری و رمز عبور الزامی است."));

    var result = await signInManager.PasswordSignInAsync(username, password, isPersistent: false, lockoutOnFailure: true);
    return result.Succeeded
        ? Results.LocalRedirect(safeReturnUrl)
        : Results.Redirect(BuildLoginUrl(safeReturnUrl, result.IsLockedOut
            ? "حساب کاربری موقتاً قفل شده است. بعداً دوباره تلاش کنید."
            : "نام کاربری یا رمز عبور نادرست است."));
}).AllowAnonymous();

app.MapGet("/Account/LogoutPost", async (HttpContext http, SignInManager<ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    http.Response.Headers.CacheControl = "no-store";
    return Results.Redirect("/Account/Login");
}).AllowAnonymous();

// 8. Files customers sent (photo, video, voice, ...). Administrators only (application level: the fallback policy; role
//    level: the Administrator role). Only allow-listed image/video/audio types are rendered inline; every other type is
//    a download, and "nosniff" stops the browser from guessing a more dangerous type, so a customer can never run a
//    page or script in the panel's origin. The Bale download URL (it contains the bot token) is never involved here.
app.MapGet("/admin/attachments/{id:int}", async (int id, bool? download, HttpContext http, ConversationService conversations, CancellationToken cancellationToken) =>
{
    var file = await conversations.GetAttachmentAsync(id, cancellationToken);
    if (file?.Content is null)
        return Results.NotFound();

    var mime = AttachmentMime.Resolve(file.Kind, file.MimeType);
    var inline = download != true && AttachmentMime.IsInlineSafe(mime);

    http.Response.Headers.XContentTypeOptions = "nosniff";
    http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
    http.Response.Headers.CacheControl = "private, max-age=3600";

    return inline
        ? Results.Bytes(file.Content, mime, enableRangeProcessing: true)
        : Results.Bytes(file.Content, "application/octet-stream", AttachmentMime.DownloadName(file.Id, file.FileName, mime));
}).RequireAuthorization(new AuthorizeAttribute { Roles = AppRoles.Administrator });

app.MapRazorComponents<App>().AddInteractiveServerRenderMode().Add(endpoint =>
{
    // The login page needs the bootstrap script, which is an endpoint rather than a wwwroot file.
    if (endpoint is RouteEndpointBuilder { RoutePattern.RawText: "/_framework/blazor.web.js" })
        endpoint.Metadata.Add(new AllowAnonymousAttribute());
});

app.Run();

static string BuildLoginUrl(string returnUrl, string error) =>
    $"/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}&error={Uri.EscapeDataString(error)}";
