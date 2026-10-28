using Telerik.Blazor.Services;

namespace SiloSupport.Web.Localization;

/// <summary>
/// Persian texts for the built-in strings of Telerik UI for Blazor components (grid, window, dialog, ...).
/// Keys that are not listed return <c>null</c>, so Telerik keeps its own default for them. Texts that this
/// application controls itself (labels, titles, buttons, messages) are written in Persian directly in the pages.
/// </summary>
public class TelerikPersianLocalizer : ITelerikStringLocalizer
{
    private static readonly Dictionary<string, string> Texts = new(StringComparer.Ordinal)
    {
        ["Grid_NoRecords"] = "رکوردی برای نمایش وجود ندارد.",
        ["Grid_Search"] = "جستجو",
        ["Grid_SortAscending"] = "مرتب‌سازی صعودی",
        ["Grid_SortDescending"] = "مرتب‌سازی نزولی",
        ["Window_Close"] = "بستن",
        ["Window_Minimize"] = "کوچک کردن",
        ["Window_Maximize"] = "بزرگ کردن",
        ["Window_Restore"] = "بازگردانی",
        ["Dialog_Close"] = "بستن",
        ["DropDownList_NoData"] = "داده‌ای وجود ندارد.",
        ["DropDownList_Filter"] = "جستجو",
        ["Pager_ItemsPerPage"] = "مورد در هر صفحه",
        ["Pager_Page"] = "صفحه",
        ["Pager_Of"] = "از",
        ["Loader_Loading"] = "در حال بارگذاری…"
    };

    public string this[string name] => GetStringFromResource(name);

    public string GetStringFromResource(string key) =>
        Texts.TryGetValue(key, out var text) ? text : null!;
}
