using System.Text.RegularExpressions;

namespace SiloSupport.Tests;

/// <summary>
/// Guards the UI conventions that are easy to lose in a refactor (RTL, bundled Vazirmatn font, drawer closed by
/// default, role-protected pages). They read the Web project's source files; the Web project itself has no tests
/// and is not referenced.
/// </summary>
public class UiConventionTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static string Web(params string[] parts) => Path.Combine(new[] { Root, "SiloSupport.Web" }.Concat(parts).ToArray());
    private static string Read(params string[] parts) => File.ReadAllText(Web(parts));

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SiloSupport.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("SiloSupport.slnx was not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void Document_IsPersianAndRightToLeft()
    {
        var app = Read("Components", "App.razor");

        Assert.Contains("<html lang=\"fa\" dir=\"rtl\">", app);
        Assert.Contains("<body dir=\"rtl\">", app);
    }

    [Fact]
    public void MainLayout_IsRightToLeftInsideTheTelerikRoot()
    {
        var layout = Read("Components", "Layout", "MainLayout.razor");

        Assert.Contains("dir=\"rtl\"", layout);
        Assert.Contains("<TelerikRootComponent>", layout);
    }

    [Theory]
    [InlineData("Vazirmatn-Regular.woff2")]
    [InlineData("Vazirmatn-Medium.woff2")]
    [InlineData("Vazirmatn-Bold.woff2")]
    [InlineData("Vazirmatn-OFL.txt")]
    public void VazirmatnFontFiles_AreBundledWithTheApp(string file)
    {
        Assert.True(File.Exists(Web("wwwroot", "fonts", file)), file);
    }

    [Fact]
    public void Stylesheet_UsesTheBundledVazirmatnFontEverywhere()
    {
        var css = Read("wwwroot", "app.css");

        foreach (Match match in Regex.Matches(css, @"url\(""(fonts/[^""]+)""\)"))
            Assert.True(File.Exists(Web("wwwroot", match.Groups[1].Value)), match.Groups[1].Value);

        Assert.Contains("font-family: \"Vazirmatn\"", css);
        Assert.Contains("--kendo-font-family: var(--app-font)", css);
        Assert.Contains("--app-font: \"Vazirmatn\"", css);
        Assert.Contains("direction: rtl", css);
    }

    [Fact]
    public void Drawer_IsClosedByDefaultAndOpensOverTheContent()
    {
        var layout = Read("Components", "Layout", "MainLayout.razor");

        Assert.Matches(@"private bool _expanded;", layout);
        Assert.DoesNotContain("_expanded = true", layout);
        Assert.Contains("DrawerMode.Overlay", layout);
        Assert.Contains("MiniMode=\"false\"", layout);
        Assert.Contains("<DrawerContent>", layout);
    }

    [Fact]
    public void Sidebar_ListsTheDashboardCustomersAndConversations_ForAdministratorsOnly()
    {
        var layout = Read("Components", "Layout", "MainLayout.razor");

        Assert.Contains("Url = \"/\"", layout);
        Assert.Contains("Url = \"/admin/customers\"", layout);
        Assert.Contains("Url = \"/admin/conversations\"", layout);
        Assert.Contains("<AuthorizeView Roles=\"@AppRoles.Administrator\">", layout);
    }

    [Fact]
    public void EveryAdminPage_RequiresTheAdministratorRole()
    {
        var pages = Directory.GetFiles(Web("Components", "Pages"), "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "Account" + Path.DirectorySeparatorChar))
            .ToList();

        Assert.NotEmpty(pages);
        foreach (var page in pages)
        {
            var text = File.ReadAllText(page);
            if (!text.Contains("@page"))
                continue;

            Assert.Contains("@attribute [Authorize(Roles = AppRoles.Administrator)]", text);
        }
    }

    [Fact]
    public void AccountPages_AreAnonymousStaticPages()
    {
        foreach (var name in new[] { "Login.razor", "AccessDenied.razor" })
        {
            var text = Read("Components", "Pages", "Account", name);
            Assert.Contains("@attribute [AllowAnonymous]", text);
            Assert.Contains("@attribute [ExcludeFromInteractiveRouting]", text);
        }
    }

    [Fact]
    public void WebProject_ContainsNoBotCode()
    {
        foreach (var file in Directory.GetFiles(Web(), "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".razor")) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("IBaleBotClient", text);
            Assert.DoesNotContain("BackgroundService", text);
            Assert.DoesNotContain("CustomerMessageProcessor", text);
        }
    }
}
