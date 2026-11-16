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
        Assert.Contains("<TelerikRootComponent EnableRtl=\"true\">", layout);
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
        Assert.Contains("Url = \"/admin/support-persons\"", layout);
        Assert.Contains("<AuthorizeView Roles=\"@AppRoles.Administrator\">", layout);
    }

    [Fact]
    public void SupportPersonPages_ExistAndUseTelerikComponents()
    {
        var list = Read("Components", "Pages", "Admin", "SupportPersons", "SupportPersonList.razor");
        var edit = Read("Components", "Pages", "Admin", "SupportPersons", "SupportPersonEdit.razor");

        Assert.Contains("@page \"/admin/support-persons\"", list);
        Assert.Contains("<TelerikGrid", list);
        Assert.Contains("@page \"/admin/support-persons/new\"", edit);
        Assert.Contains("@page \"/admin/support-persons/{Id:int}/edit\"", edit);
        Assert.Contains("<TelerikTextBox", edit);
        Assert.DoesNotContain("<input", edit);
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
    public void ConversationsPage_ShowsIssueThreadsAndFiles_WithTelerikComponents()
    {
        var page = Read("Components", "Pages", "Admin", "Conversations", "CustomerConversations.razor");

        Assert.Contains("[Authorize(Roles = AppRoles.Administrator)]", page);
        Assert.Contains("<TelerikTabStrip>", page);
        Assert.Contains("<TelerikSwitch", page);
        Assert.Contains("<TelerikGrid", page);
        Assert.Contains("<AttachmentView", page);
        Assert.Contains("SetThreadResolvedAsync", page);
        Assert.DoesNotContain("<input", page);
    }

    [Fact]
    public void ConversationsPage_IsBuiltAroundThreads_WithSummaryConversationFilterAndTheOldGroupView()
    {
        var page = Read("Components", "Pages", "Admin", "Conversations", "CustomerConversations.razor");

        Assert.Contains("thread.Summary", page);          // the rolling summary of each issue
        Assert.Contains("<Conversation Messages=", page); // chat-style conversation under each thread
        Assert.Contains("VisibleThreads", page);          // status filter + search
        Assert.Contains("<TelerikTextBox", page);
        Assert.Contains("<TelerikButton", page);
        Assert.Contains("پیام‌های قدیمی", page);           // messages from before threads existed stay visible
        Assert.Contains("بر اساس گروه اولویت", page);     // the per-group view with per-message resolve is kept
        Assert.Contains("SetResolvedAsync", page);
        Assert.DoesNotContain("OpenWindowHours", page);
    }

    [Fact]
    public void ConversationComponent_ShowsTheCustomersTextTheFileAndTheForwardedText()
    {
        var view = Read("Components", "Shared", "Conversation.razor");

        Assert.Contains("OriginalText", view);
        Assert.Contains("<AttachmentView", view);
        Assert.Contains("<PriorityChip", view);
        Assert.Contains("PersianDateFormatter", view);
    }

    [Fact]
    public void AttachmentView_UsesTheSecuredEndpointAndOnlyPlaysSafeTypes()
    {
        var view = Read("Components", "Shared", "AttachmentView.razor");

        Assert.Contains("/admin/attachments/", view);
        Assert.Contains("AttachmentMime.PreviewKind", view);
        Assert.Contains("<video", view);
        Assert.Contains("<audio", view);
        Assert.DoesNotContain("<iframe", view);
        Assert.DoesNotContain("<object", view);
        Assert.DoesNotContain("<embed", view);
    }

    [Fact]
    public void AttachmentEndpoint_IsAdministratorOnly_AndServesFilesSafely()
    {
        var program = Read("Program.cs");

        Assert.Contains("MapGet(\"/admin/attachments/{id:int}\"", program);
        Assert.Contains("RequireAuthorization(new AuthorizeAttribute { Roles = AppRoles.Administrator })", program);
        Assert.Contains("AttachmentMime.IsInlineSafe", program);
        Assert.Contains("nosniff", program);
        Assert.Contains("application/octet-stream", program);
    }

    [Fact]
    public void ThreadAndFileStyles_Exist()
    {
        var css = Read("wwwroot", "app.css");

        Assert.Contains(".thread-timeline", css);
        Assert.Contains(".attachment-image", css);
        Assert.Contains(".attachment-video", css);
        Assert.Contains(".chat-bubble", css);
        Assert.Contains(".thread-summary", css);
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
