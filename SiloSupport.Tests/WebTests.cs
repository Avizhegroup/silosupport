using System.Net;
using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using SiloSupport.Application.Services;
using SiloSupport.Domain.Enums;
using SiloSupport.Tests.Support;
using SiloSupport.Web.Components.Pages.Account;
using SiloSupport.Web.Components.Pages.Admin.Conversations;
using SiloSupport.Web.Components.Pages.Admin.Customers;
using SiloSupport.Web.Components.Pages.Admin.Groups;
using SiloSupport.Web.Components.Pages.Admin.Members;
using SiloSupport.Web.Security;

namespace SiloSupport.Tests;

public class ReturnUrlHelperTests
{
    [Theory]
    [InlineData("/admin/customers", true)]
    [InlineData("/admin/conversations/3?x=1", true)]
    [InlineData("/", true)]
    [InlineData("//evil.example", false)]
    [InlineData("/\\evil.example", false)]
    [InlineData("https://evil.example", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("/ok\r\nSet-Cookie: x=1", false)]
    public void IsSafe_OnlyAcceptsLocalPaths(string? url, bool expected)
    {
        Assert.Equal(expected, ReturnUrlHelper.IsSafe(url));
    }

    [Fact]
    public void Resolve_UnsafeUrl_FallsBackToTheDefault()
    {
        Assert.Equal(ReturnUrlHelper.DefaultUrl, ReturnUrlHelper.Resolve("https://evil.example"));
    }
}

/// <summary>Reflection on the compiled components: roles, anonymous access and routes.</summary>
public class PageAccessTests
{
    public static IEnumerable<object[]> AdminPages() => new[]
    {
        new object[] { typeof(CustomerList), "/admin/customers" },
        new object[] { typeof(CustomerEdit), "/admin/customers/new" },
        new object[] { typeof(CustomerDetails), "/admin/customers/{Id:int}" },
        new object[] { typeof(MemberEdit), "/admin/customers/{CustomerId:int}/members/new" },
        new object[] { typeof(GroupEdit), "/admin/customers/{CustomerId:int}/groups/new" },
        new object[] { typeof(CustomerConversations), "/admin/conversations" },
        new object[] { typeof(CustomerConversations), "/admin/conversations/{CustomerId:int}" },
    };

    [Theory]
    [MemberData(nameof(AdminPages))]
    public void AdminPage_RequiresTheAdministratorRole_AndHasItsRoute(Type page, string route)
    {
        var authorize = page.GetCustomAttributes<AuthorizeAttribute>().SingleOrDefault();

        Assert.NotNull(authorize);
        Assert.Equal(AppRoles.Administrator, authorize!.Roles);
        Assert.Contains(page.GetCustomAttributes<RouteAttribute>(), r => r.Template == route);
        Assert.Empty(page.GetCustomAttributes<AllowAnonymousAttribute>());
    }

    [Theory]
    [InlineData(typeof(Login), "/Account/Login")]
    [InlineData(typeof(AccessDenied), "/Account/AccessDenied")]
    public void AccountPage_IsAnonymousAndStatic(Type page, string route)
    {
        Assert.NotEmpty(page.GetCustomAttributes<AllowAnonymousAttribute>());
        Assert.Contains(page.GetCustomAttributes<RouteAttribute>(), r => r.Template == route);
    }
}

/// <summary>bUnit: the conversations page shows only the selected customer's messages, grouped by destination group.</summary>
public class CustomerConversationsPageTests : TestBase
{
    private readonly Bunit.TestContext _ctx = new();

    public CustomerConversationsPageTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton(Get<CustomerService>());
        _ctx.Services.AddSingleton(Get<ConversationService>());
        _ctx.RenderComponent<MudPopoverProvider>();
    }

    [Fact]
    public async Task Page_ShowsOnlyTheSelectedCustomersMessages_GroupedByGroupWithPriorityAndStatus()
    {
        var a = await AddCustomerAsync("Alpha");
        var b = await AddCustomerAsync("Beta");
        var aLow = await AddGroupAsync(a.Id, GroupPriority.Low, 10, "Alpha low group");
        var aCritical = await AddGroupAsync(a.Id, GroupPriority.Critical, 11, "Alpha critical group");
        var bLow = await AddGroupAsync(b.Id, GroupPriority.Low, 20, "Beta low group");
        await AddMessageAsync(aLow.Id, "alpha low message");
        var resolved = await AddMessageAsync(aCritical.Id, "alpha critical message");
        await Get<ConversationService>().SetResolvedAsync(a.Id, resolved.Id, true);
        await AddMessageAsync(bLow.Id, "beta private message");

        var page = _ctx.RenderComponent<CustomerConversations>(p => p.Add(x => x.CustomerId, a.Id));

        var markup = page.Markup;
        Assert.Contains("Alpha low group", markup);
        Assert.Contains("Alpha critical group", markup);
        Assert.Contains("alpha low message", markup);
        Assert.Contains("alpha critical message", markup);
        Assert.Contains("Critical", markup);
        Assert.Contains("Resolved", markup);
        Assert.Contains("Unresolved", markup);
        Assert.DoesNotContain("beta private message", markup);
        Assert.DoesNotContain("Beta low group", markup);
    }

    [Fact]
    public async Task Page_SwitchingCustomerLoadsTheOtherCustomersMessages()
    {
        var a = await AddCustomerAsync("Alpha");
        var b = await AddCustomerAsync("Beta");
        var aGroup = await AddGroupAsync(a.Id, GroupPriority.Low, 10, "Alpha group");
        var bGroup = await AddGroupAsync(b.Id, GroupPriority.Low, 20, "Beta group");
        await AddMessageAsync(aGroup.Id, "alpha message");
        await AddMessageAsync(bGroup.Id, "beta message");

        var page = _ctx.RenderComponent<CustomerConversations>(p => p.Add(x => x.CustomerId, a.Id));
        Assert.Contains("alpha message", page.Markup);

        page.SetParametersAndRender(p => p.Add(x => x.CustomerId, b.Id));

        Assert.Contains("beta message", page.Markup);
        Assert.DoesNotContain("alpha message", page.Markup);
    }

    [Fact]
    public async Task Page_WithoutSelectedCustomer_ShowsAHint()
    {
        await AddCustomerAsync("Alpha");

        var page = _ctx.RenderComponent<CustomerConversations>();

        Assert.Contains("Select a customer", page.Markup);
    }

    [Fact]
    public async Task Page_CustomerWithMissingGroups_ShowsWhichPrioritiesAreMissing()
    {
        var a = await AddCustomerAsync("Alpha");
        await AddGroupAsync(a.Id, GroupPriority.Low, 10);

        var page = _ctx.RenderComponent<CustomerConversations>(p => p.Add(x => x.CustomerId, a.Id));

        Assert.Contains("Missing priority group", page.Markup);
        Assert.Contains("Critical", page.Markup);
        Assert.Contains("Medium", page.Markup);
    }

    public override void Dispose()
    {
        _ctx.Dispose();
        base.Dispose();
    }
}

/// <summary>Endpoint tests with the real host on a throw-away SQLite file; the Bale worker is disabled.</summary>
public class WebHostTests : IClassFixture<WebHostTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"silosupport-test-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting("Database:ConnectionString", $"DataSource={_dbPath}");
            builder.UseSetting("Bale:Enabled", "false");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { File.Delete(_dbPath); } catch { /* best effort */ }
        }
    }

    private readonly Factory _factory;

    public WebHostTests(Factory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Theory]
    [InlineData("/admin/customers")]
    [InlineData("/admin/conversations")]
    [InlineData("/admin/customers/1")]
    public async Task AdminPages_RedirectAnonymousVisitorsToLogin(string url)
    {
        var response = await NewClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task LoginPage_IsPublic_AndPostsToTheLoginEndpointWithAnAntiforgeryToken()
    {
        var response = await NewClient().GetAsync("/Account/Login");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("action=\"/Account/LoginPost\"", html);
        Assert.Contains("__RequestVerificationToken", html);
    }

    [Fact]
    public async Task LoginEndpoint_RejectsPostsWithoutAnAntiforgeryToken()
    {
        var response = await NewClient().PostAsync("/Account/LoginPost", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "x",
            ["password"] = "y"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
